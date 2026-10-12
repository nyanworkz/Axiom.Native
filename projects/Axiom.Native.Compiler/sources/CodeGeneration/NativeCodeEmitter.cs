using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil;

namespace Axiom.Native;

public static class NativeCodeEmitter
{
	public static NativeEmissionResult EmitObjectFile(
		LlvmIrGenerationResult irResult,
		NativeEmissionOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(irResult);

		if (!irResult.IsSuccess || string.IsNullOrWhiteSpace(irResult.LlvmIr))
		{
			var diag = new NativeEmissionDiagnostic(
				"AXM4001",
				DiagnosticSeverity.Error,
				"Cannot emit native code: LLVM IR generation result is unsuccessful or contains no IR.",
				irResult.Method?.FullName ?? "Unknown"
			);
			return new NativeEmissionResult(irResult.Method, null, new[] { diag });
		}

		return EmitObjectFile(irResult.LlvmIr, irResult.Method, options);
	}

	public static NativeEmissionResult EmitObjectFile(
		string llvmIr,
		MethodDefinition? method,
		NativeEmissionOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(llvmIr);

		options ??= NativeEmissionOptions.Default;
		var diagnostics = new List<NativeEmissionDiagnostic>();
		var methodName = method?.FullName ?? "AxiomKernel";

		// 1. Verify LLVM IR if requested and opt is available
		if (options.VerifyIr)
		{
			var optPath = LlvmToolchain.FindTool("opt", options.ToolchainPath);
			if (optPath is not null)
			{
				var verifyResult = VerifyIr(llvmIr, optPath, options.TimeoutMs, methodName);
				if (verifyResult is not null)
				{
					diagnostics.Add(verifyResult);
					return new NativeEmissionResult(method, null, diagnostics);
				}
			}
		}

		// 2. Discover backend compiler tool (clang or llc)
		var (toolPath, isClang) = ResolveBackendTool(options);
		if (toolPath is null)
		{
			diagnostics.Add(
				new NativeEmissionDiagnostic(
					"AXM4003",
					DiagnosticSeverity.Error,
					"No supported LLVM compiler backend (clang or llc) was found. Set AXIOM_LLVM_PATH or configure ToolchainPath.",
					methodName
				)
			);
			return new NativeEmissionResult(method, null, diagnostics);
		}

		var toolName = Path.GetFileNameWithoutExtension(toolPath);

		// 3. Resolve destination output path
		string outputPath;
		try
		{
			outputPath = ResolveOutputPath(options.OutputPath, methodName);
			var outDir = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrWhiteSpace(outDir))
			{
				Directory.CreateDirectory(outDir);
			}

			// Clean existing destination file if present
			if (File.Exists(outputPath))
			{
				File.Delete(outputPath);
			}
		}
		catch (Exception ex)
		{
			diagnostics.Add(
				new NativeEmissionDiagnostic(
					"AXM4006",
					DiagnosticSeverity.Error,
					$"Invalid output path or directory cannot be created: {ex.Message}",
					methodName,
					toolName
				)
			);
			return new NativeEmissionResult(method, null, diagnostics);
		}

		// 4. Create temporary input file and invoke compiler
		var tempInputFile = Path.Combine(Path.GetTempPath(), $"axiom_src_{Guid.NewGuid():N}.ll");
		try
		{
			File.WriteAllText(tempInputFile, llvmIr, Encoding.ASCII);

			var targetTriple = options.TargetTriple ?? LlvmToolchain.GetHostTargetTriple();
			var optFlag = LlvmToolchain.GetOptimizationFlag(options.Optimization, isClang);

			var arguments = new List<string>();
			if (isClang)
			{
				// clang -c <input.ll> -o <output.obj> <optFlag> -target <triple>
				arguments.Add("-c");
				arguments.Add(tempInputFile);
				arguments.Add("-o");
				arguments.Add(outputPath);
				arguments.Add(optFlag);
				if (!string.IsNullOrWhiteSpace(targetTriple))
				{
					arguments.Add("-target");
					arguments.Add(targetTriple);
				}
			}
			else
			{
				// llc -filetype=obj <input.ll> -o <output.obj> <optFlag> -mtriple=<triple>
				arguments.Add("-filetype=obj");
				arguments.Add(tempInputFile);
				arguments.Add("-o");
				arguments.Add(outputPath);
				arguments.Add(optFlag);
				if (!string.IsNullOrWhiteSpace(targetTriple))
				{
					arguments.Add($"-mtriple={targetTriple}");
				}
			}

			var execResult = RunProcess(toolPath, arguments, options.TimeoutMs);

			if (execResult.TimedOut)
			{
				diagnostics.Add(
					new NativeEmissionDiagnostic(
						"AXM4007",
						DiagnosticSeverity.Error,
						$"Compiler '{toolName}' timed out after {options.TimeoutMs} ms.",
						methodName,
						toolName
					)
				);
				return new NativeEmissionResult(
					method,
					null,
					diagnostics,
					execResult.StandardOutput,
					execResult.StandardError,
					-1
				);
			}

			if (execResult.ExitCode != 0)
			{
				diagnostics.Add(
					new NativeEmissionDiagnostic(
						"AXM4004",
						DiagnosticSeverity.Error,
						$"Compiler '{toolName}' failed with exit code {execResult.ExitCode}: {execResult.StandardError.Trim()}",
						methodName,
						toolName,
						execResult.ExitCode
					)
				);
				return new NativeEmissionResult(
					method,
					null,
					diagnostics,
					execResult.StandardOutput,
					execResult.StandardError,
					execResult.ExitCode
				);
			}

			// 5. Verify output file exists and is non-empty
			if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
			{
				diagnostics.Add(
					new NativeEmissionDiagnostic(
						"AXM4005",
						DiagnosticSeverity.Error,
						$"Expected native object file was not created or is empty at '{outputPath}'.",
						methodName,
						toolName,
						execResult.ExitCode
					)
				);
				return new NativeEmissionResult(
					method,
					null,
					diagnostics,
					execResult.StandardOutput,
					execResult.StandardError,
					execResult.ExitCode
				);
			}

			return new NativeEmissionResult(
				method,
				outputPath,
				diagnostics,
				execResult.StandardOutput,
				execResult.StandardError,
				execResult.ExitCode
			);
		}
		catch (Exception ex)
		{
			diagnostics.Add(
				new NativeEmissionDiagnostic(
					"AXM4008",
					DiagnosticSeverity.Error,
					$"Unexpected error during native emission: {ex.Message}",
					methodName,
					toolName
				)
			);
			return new NativeEmissionResult(method, null, diagnostics);
		}
		finally
		{
			if (File.Exists(tempInputFile))
			{
				try
				{
					File.Delete(tempInputFile);
				}
				catch
				{
					// Ignore temp file cleanup errors
				}
			}
		}
	}

	private static NativeEmissionDiagnostic? VerifyIr(
		string llvmIr,
		string optPath,
		int timeoutMs,
		string methodName
	)
	{
		var tempFile = Path.Combine(Path.GetTempPath(), $"axiom_verify_{Guid.NewGuid():N}.ll");
		try
		{
			File.WriteAllText(tempFile, llvmIr, Encoding.ASCII);
			var nullOutput = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? "NUL"
				: "/dev/null";
			var args = new[] { "-passes=verify", tempFile, "-o", nullOutput };

			var exec = RunProcess(optPath, args, timeoutMs);
			if (exec.TimedOut)
			{
				return new NativeEmissionDiagnostic(
					"AXM4007",
					DiagnosticSeverity.Error,
					$"LLVM verifier 'opt' timed out after {timeoutMs} ms.",
					methodName,
					"opt"
				);
			}

			if (exec.ExitCode != 0)
			{
				return new NativeEmissionDiagnostic(
					"AXM4002",
					DiagnosticSeverity.Error,
					$"LLVM IR verification failed (exit code {exec.ExitCode}): {exec.StandardError.Trim()}",
					methodName,
					"opt",
					exec.ExitCode
				);
			}

			return null;
		}
		catch (Exception ex)
		{
			return new NativeEmissionDiagnostic(
				"AXM4002",
				DiagnosticSeverity.Error,
				$"LLVM IR verification error: {ex.Message}",
				methodName,
				"opt"
			);
		}
		finally
		{
			if (File.Exists(tempFile))
			{
				try
				{
					File.Delete(tempFile);
				}
				catch
				{
					// Ignore cleanup errors
				}
			}
		}
	}

	private static (string? Path, bool IsClang) ResolveBackendTool(NativeEmissionOptions options)
	{
		if (options.PreferredTool == LlvmBackendTool.Clang)
		{
			var clang = LlvmToolchain.FindTool("clang", options.ToolchainPath);
			if (clang is not null)
				return (clang, true);
		}
		else if (options.PreferredTool == LlvmBackendTool.Llc)
		{
			var llc = LlvmToolchain.FindTool("llc", options.ToolchainPath);
			if (llc is not null)
				return (llc, false);
		}
		else
		{
			// Auto: prefer clang if available, fallback to llc
			var clang = LlvmToolchain.FindTool("clang", options.ToolchainPath);
			if (clang is not null)
				return (clang, true);

			var llc = LlvmToolchain.FindTool("llc", options.ToolchainPath);
			if (llc is not null)
				return (llc, false);
		}

		return (null, false);
	}

	private static string ResolveOutputPath(string? specifiedPath, string methodName)
	{
		if (!string.IsNullOrWhiteSpace(specifiedPath))
		{
			return Path.GetFullPath(specifiedPath);
		}

		// Sanitize method name for filesystem
		var sanitized = new StringBuilder();
		foreach (var ch in methodName)
		{
			if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
			{
				sanitized.Append(ch);
			}
			else
			{
				sanitized.Append('_');
			}
		}

		var ext = LlvmToolchain.GetHostObjectExtension();
		var baseDir = Path.Combine(Path.GetTempPath(), "AxiomNative", "Objects");
		return Path.Combine(baseDir, $"{sanitized}{ext}");
	}

	public record ProcessExecutionResult(
		int ExitCode,
		string StandardOutput,
		string StandardError,
		bool TimedOut = false
	);

	public static ProcessExecutionResult RunProcess(
		string executablePath,
		IReadOnlyList<string> arguments,
		int timeoutMs = 30000
	)
	{
		ArgumentNullException.ThrowIfNull(executablePath);
		ArgumentNullException.ThrowIfNull(arguments);

		var psi = new ProcessStartInfo(executablePath)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		foreach (var arg in arguments)
		{
			psi.ArgumentList.Add(arg);
		}

		using var proc = new Process { StartInfo = psi };
		try
		{
			proc.Start();
		}
		catch (Exception ex)
		{
			return new ProcessExecutionResult(
				-1,
				"",
				$"Failed to start process '{executablePath}': {ex.Message}"
			);
		}

		var stdoutTask = proc.StandardOutput.ReadToEndAsync();
		var stderrTask = proc.StandardError.ReadToEndAsync();

		var exited = proc.WaitForExit(timeoutMs);
		if (!exited)
		{
			try
			{
				proc.Kill(entireProcessTree: true);
			}
			catch
			{
				// Ignore kill errors
			}
			return new ProcessExecutionResult(-1, "", "Process timed out.", TimedOut: true);
		}

		Task.WaitAll(new Task[] { stdoutTask, stderrTask }, TimeSpan.FromMilliseconds(5000));
		return new ProcessExecutionResult(proc.ExitCode, stdoutTask.Result, stderrTask.Result);
	}
}

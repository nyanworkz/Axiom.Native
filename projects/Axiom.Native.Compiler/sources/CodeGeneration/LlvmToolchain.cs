using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Axiom.Native;

public static class LlvmToolchain
{
	public static string GetHostTargetTriple()
	{
		var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
		var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
		var isOsx = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
		var arch = RuntimeInformation.ProcessArchitecture;

		if (isWindows)
		{
			return arch switch
			{
				Architecture.X64 => "x86_64-pc-windows-msvc",
				Architecture.Arm64 => "aarch64-pc-windows-msvc",
				Architecture.X86 => "i686-pc-windows-msvc",
				_ => "x86_64-pc-windows-msvc",
			};
		}

		if (isLinux)
		{
			return arch switch
			{
				Architecture.X64 => "x86_64-unknown-linux-gnu",
				Architecture.Arm64 => "aarch64-unknown-linux-gnu",
				_ => "x86_64-unknown-linux-gnu",
			};
		}

		if (isOsx)
		{
			return arch switch
			{
				Architecture.Arm64 => "aarch64-apple-darwin",
				Architecture.X64 => "x86_64-apple-darwin",
				_ => "x86_64-apple-darwin",
			};
		}

		return "x86_64-unknown-unknown";
	}

	public static string GetHostObjectExtension()
	{
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".obj" : ".o";
	}

	public static string? FindTool(string toolName, string? customDirectory = null)
	{
		ArgumentNullException.ThrowIfNull(toolName);

		var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
		var binaryName =
			isWindows && !toolName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
				? toolName + ".exe"
				: toolName;

		// 1. Explicit custom directory: if specified, restrict lookup to this directory
		if (!string.IsNullOrWhiteSpace(customDirectory))
		{
			var candidate = Path.Combine(customDirectory, binaryName);
			return File.Exists(candidate) ? candidate : null;
		}

		// 2. Environment variables: AXIOM_LLVM_PATH, AXIOM_LLVM_BIN, LLVM_HOME, LLVM_PATH
		var envVars = new[] { "AXIOM_LLVM_PATH", "AXIOM_LLVM_BIN", "LLVM_HOME", "LLVM_PATH" };
		foreach (var envVar in envVars)
		{
			var dir = Environment.GetEnvironmentVariable(envVar);
			if (!string.IsNullOrWhiteSpace(dir))
			{
				var candidate = Path.Combine(dir, binaryName);
				if (File.Exists(candidate))
				{
					return candidate;
				}
				// Also check subfolder bin
				candidate = Path.Combine(dir, "bin", binaryName);
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
		}

		// 3. System PATH search
		var pathEnv = Environment.GetEnvironmentVariable("PATH");
		if (!string.IsNullOrWhiteSpace(pathEnv))
		{
			var separator = isWindows ? ';' : ':';
			foreach (
				var segment in pathEnv.Split(
					separator,
					StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
				)
			)
			{
				try
				{
					var candidate = Path.Combine(segment, binaryName);
					if (File.Exists(candidate))
					{
						return candidate;
					}
				}
				catch
				{
					// Ignore invalid paths in PATH variable
				}
			}
		}

		// 4. Well-known fallback locations
		if (isWindows)
		{
			var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			var candidate = Path.Combine(programFiles, "LLVM", "bin", binaryName);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
		else
		{
			var candidates = new[]
			{
				Path.Combine("/usr/bin", binaryName),
				Path.Combine("/usr/local/bin", binaryName),
				Path.Combine("/opt/homebrew/bin", binaryName),
			};
			foreach (var candidate in candidates)
			{
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
		}

		return null;
	}

	public static string GetOptimizationFlag(OptimizeFor strategy, bool isClang)
	{
		return strategy switch
		{
			OptimizeFor.Debug => "-O0",
			OptimizeFor.ExecutionSpeed => "-O3",
			OptimizeFor.BuildSize => isClang ? "-Oz" : "-O2",
			_ => "-O3",
		};
	}
}

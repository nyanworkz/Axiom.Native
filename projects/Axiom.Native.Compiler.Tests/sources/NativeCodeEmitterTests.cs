using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Axiom.Native.Compiler.Tests;

public class NativeCodeEmitterTests
{
	private static string GetTestAssemblyPath()
	{
		var path = Path.Combine(
			AppContext.BaseDirectory,
			"Axiom.Native.Compiler.Tests.TestAssembly.dll"
		);
		Assert.True(File.Exists(path), $"Test assembly not found at: {path}");
		return path;
	}

	private static void WithMethod(
		string typeName,
		string methodName,
		Action<MethodDefinition> action
	)
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);
		var type = context.Assembly.MainModule.Types.FirstOrDefault(t => t.Name == typeName);
		Assert.NotNull(type);

		var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
		Assert.NotNull(method);
		action(method);
	}

	private static bool IsLlvmBackendAvailable()
	{
		return LlvmToolchain.FindTool("clang") is not null
			|| LlvmToolchain.FindTool("llc") is not null;
	}

	// ==========================================
	// 1. Toolchain discovery & configuration
	// ==========================================
	[Fact]
	public void Toolchain_01_HostTargetTriple_IsNonEmptyAndValid()
	{
		var triple = LlvmToolchain.GetHostTargetTriple();
		Assert.False(string.IsNullOrWhiteSpace(triple));
		Assert.True(
			triple.Contains("windows", StringComparison.OrdinalIgnoreCase)
				|| triple.Contains("linux", StringComparison.OrdinalIgnoreCase)
				|| triple.Contains("darwin", StringComparison.OrdinalIgnoreCase)
				|| triple.Contains("unknown", StringComparison.OrdinalIgnoreCase)
		);
	}

	[Fact]
	public void Toolchain_02_HostObjectExtension_MatchesPlatform()
	{
		var ext = LlvmToolchain.GetHostObjectExtension();
		Assert.True(ext == ".obj" || ext == ".o");
	}

	[Fact]
	public void Toolchain_03_OptimizationFlags_MapFaithfully()
	{
		Assert.Equal("-O0", LlvmToolchain.GetOptimizationFlag(OptimizeFor.Debug, isClang: true));
		Assert.Equal("-O0", LlvmToolchain.GetOptimizationFlag(OptimizeFor.Debug, isClang: false));

		Assert.Equal(
			"-O3",
			LlvmToolchain.GetOptimizationFlag(OptimizeFor.ExecutionSpeed, isClang: true)
		);
		Assert.Equal(
			"-O3",
			LlvmToolchain.GetOptimizationFlag(OptimizeFor.ExecutionSpeed, isClang: false)
		);

		Assert.Equal(
			"-Oz",
			LlvmToolchain.GetOptimizationFlag(OptimizeFor.BuildSize, isClang: true)
		);
		Assert.Equal(
			"-O2",
			LlvmToolchain.GetOptimizationFlag(OptimizeFor.BuildSize, isClang: false)
		);
	}

	[Fact]
	public void Toolchain_04_CustomPath_LocatesBinaryIfExists()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), $"axiom_fake_toolchain_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		try
		{
			var fakeTool = Path.Combine(tempDir, "fake_llc.exe");
			File.WriteAllText(fakeTool, "fake");

			var found = LlvmToolchain.FindTool("fake_llc", tempDir);
			Assert.NotNull(found);
			Assert.Equal(fakeTool, found);
		}
		finally
		{
			Directory.Delete(tempDir, recursive: true);
		}
	}

	// ==========================================
	// 2. Unit tests: Error diagnostics without requiring LLVM
	// ==========================================
	[Fact]
	public void Emission_01_InvalidIrResult_ReportsAXM4001()
	{
		var failedIr = new LlvmIrGenerationResult(
			null,
			null,
			new[]
			{
				new LlvmIrDiagnostic(
					"AXM3001",
					DiagnosticSeverity.Error,
					"Fake error",
					"FakeMethod"
				),
			}
		);

		var result = NativeCodeEmitter.EmitObjectFile(failedIr);

		Assert.False(result.IsSuccess);
		Assert.Null(result.ObjectFilePath);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM4001");
	}

	[Fact]
	public void Emission_02_MissingBackendTools_ReportsAXM4003()
	{
		var options = new NativeEmissionOptions
		{
			ToolchainPath = Path.Combine(Path.GetTempPath(), "NonExistentToolchain_Dir_999"),
			VerifyIr = false,
		};

		var validIr = "define i32 @test() { ret i32 42 }";
		var result = NativeCodeEmitter.EmitObjectFile(validIr, null, options);

		Assert.False(result.IsSuccess);
		Assert.Null(result.ObjectFilePath);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM4003");
	}

	[Fact]
	public void Emission_03_DiagnosticPropertiesAndEquality()
	{
		var diag1 = new NativeEmissionDiagnostic(
			"AXM4004",
			DiagnosticSeverity.Error,
			"Compilation failed",
			"Namespace.Class.Method",
			"clang",
			1
		);

		var diag2 = new NativeEmissionDiagnostic(
			"AXM4004",
			DiagnosticSeverity.Error,
			"Compilation failed",
			"Namespace.Class.Method",
			"clang",
			1
		);

		Assert.Equal(diag1, diag2);
		Assert.Equal(diag1.GetHashCode(), diag2.GetHashCode());
		Assert.Contains("[AXM4004][Error]", diag1.ToString());
		Assert.Contains("clang exit 1", diag1.ToString());
		Assert.Equal(0, diag1.CompareTo(diag2));
	}

	// ==========================================
	// 3. Integration tests with LLVM toolchain
	// ==========================================
	[Fact]
	public void Integration_01_EmitObjectFile_ProducesValidObjectFile()
	{
		if (!IsLlvmBackendAvailable())
		{
			// Skip integration test when LLVM tools are unavailable
			return;
		}

		WithMethod(
			"IlAnalysisPositiveSamples",
			"Add",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var irResult = LlvmIrGenerator.Generate(analysis);
				Assert.True(irResult.IsSuccess);

				var customOutput = Path.Combine(
					Path.GetTempPath(),
					$"test_add_{Guid.NewGuid():N}{LlvmToolchain.GetHostObjectExtension()}"
				);

				try
				{
					var options = new NativeEmissionOptions
					{
						OutputPath = customOutput,
						Optimization = OptimizeFor.ExecutionSpeed,
					};

					var result = NativeCodeEmitter.EmitObjectFile(irResult, options);

					Assert.True(result.IsSuccess);
					Assert.Empty(result.Diagnostics);
					Assert.NotNull(result.ObjectFilePath);
					Assert.True(File.Exists(result.ObjectFilePath));
					Assert.True(new FileInfo(result.ObjectFilePath).Length > 0);
				}
				finally
				{
					if (File.Exists(customOutput))
					{
						File.Delete(customOutput);
					}
				}
			}
		);
	}

	[Fact]
	public void Integration_02_NativeCompilerFacade_EndToEndEmission()
	{
		if (!IsLlvmBackendAvailable())
		{
			return;
		}

		WithMethod(
			"IlAnalysisPositiveSamples",
			"SumLoop",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var customOutput = Path.Combine(
					Path.GetTempPath(),
					$"test_loop_{Guid.NewGuid():N}{LlvmToolchain.GetHostObjectExtension()}"
				);

				try
				{
					var options = new NativeEmissionOptions
					{
						OutputPath = customOutput,
						Optimization = OptimizeFor.BuildSize,
					};

					var result = NativeCompiler.EmitNativeObject(analysis, options);

					Assert.True(result.IsSuccess);
					Assert.Empty(result.Diagnostics);
					Assert.NotNull(result.ObjectFilePath);
					Assert.True(File.Exists(result.ObjectFilePath));
					Assert.True(new FileInfo(result.ObjectFilePath).Length > 0);
				}
				finally
				{
					if (File.Exists(customOutput))
					{
						File.Delete(customOutput);
					}
				}
			}
		);
	}

	[Fact]
	public void Integration_03_MalformedIr_VerificationFailsWithAXM4002()
	{
		if (LlvmToolchain.FindTool("opt") is null)
		{
			return;
		}

		// Malformed IR: function returns i32, but ret returns i64
		var badIr =
			@"
define i32 @broken() {
  ret i64 42
}
";
		var options = new NativeEmissionOptions { VerifyIr = true };

		var result = NativeCodeEmitter.EmitObjectFile(badIr, null, options);

		Assert.False(result.IsSuccess);
		Assert.Null(result.ObjectFilePath);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM4002");
	}
}

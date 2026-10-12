using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Axiom.Native.Compiler.Tests;

public class LlvmIrGeneratorTests
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

	private static string? FindOptBinary()
	{
		return LlvmToolchain.FindTool("opt");
	}

	private static void AssertValidWithLlvmOptIfAvailable(string llvmIr)
	{
		var optPath = FindOptBinary();
		if (optPath is null)
		{
			return; // LLVM tooling not available in current environment
		}

		var tempFile = Path.Combine(Path.GetTempPath(), $"axiom_test_{Guid.NewGuid():N}.ll");
		try
		{
			File.WriteAllText(tempFile, llvmIr);

			var psi = new ProcessStartInfo(optPath, $"-passes=verify \"{tempFile}\" -o NUL")
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			using var proc = Process.Start(psi);
			Assert.NotNull(proc);
			proc.WaitForExit(10000);
			var stderr = proc.StandardError.ReadToEnd();
			var stdout = proc.StandardOutput.ReadToEnd();

			Assert.True(
				proc.ExitCode == 0,
				$"LLVM verifier failed (exit code {proc.ExitCode}):\nStdout: {stdout}\nStderr: {stderr}\nIR:\n{llvmIr}"
			);
		}
		finally
		{
			if (File.Exists(tempFile))
			{
				File.Delete(tempFile);
			}
		}
	}

	// ==========================================
	// 1. Constant-returning method
	// ==========================================
	[Fact]
	public void Generate_01_ReturnConstant_GeneratesValidIr()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"ReturnConstant",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains(
					"define i32 @\"System.Int32 Axiom.Native.Compiler.Tests.TestAssembly.IlAnalysisPositiveSamples::ReturnConstant()\"()",
					result.LlvmIr
				);
				Assert.Contains("ret i32 42", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 2. Methods with I4 arguments & Arithmetic
	// ==========================================
	[Fact]
	public void Generate_02_Add_GeneratesValidIr()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"Add",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("i32 %arg_0", result.LlvmIr);
				Assert.Contains("i32 %arg_1", result.LlvmIr);
				Assert.Contains("add i32 %arg_0, %arg_1", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 3. Methods with I8 arguments
	// ==========================================
	[Fact]
	public void Generate_03_AddStaticArgs_GeneratesValidIr()
	{
		WithMethod(
			"StaticArgsSample",
			"AddStaticArgs",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("define i64", result.LlvmIr);
				Assert.Contains("i32 %arg_0", result.LlvmIr);
				Assert.Contains("i64 %arg_1", result.LlvmIr);
				Assert.Contains("sext i32 %arg_0 to i64", result.LlvmIr);
				Assert.Contains("add i64", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 4. Local variables load and store
	// ==========================================
	[Fact]
	public void Generate_04_LocalsMethod_GeneratesAllocasAndStores()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"LocalsMethod",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				// Must allocate locals in entry
				Assert.Contains("%loc_0 = alloca i32, align 4", result.LlvmIr);
				Assert.Contains("%loc_1 = alloca i32, align 4", result.LlvmIr);
				Assert.Contains("store i32", result.LlvmIr);
				Assert.Contains("load i32", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 5. Arithmetic operations
	// ==========================================
	[Fact]
	public void Generate_05_ComplexArithmetic_GeneratesValidOperations()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"ComplexArithmetic",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("add i32", result.LlvmIr);
				Assert.Contains("mul i32", result.LlvmIr);
				Assert.Contains("sub i32", result.LlvmIr);
				Assert.Contains("sdiv i32", result.LlvmIr);
				Assert.Contains("srem i32", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 6. Signed and unsigned division and remainder
	// ==========================================
	[Fact]
	public void Generate_06_SignedAndUnsignedDivRem_GeneratesCorrectInstructions()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"DivAndRem",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("sdiv i32", result.LlvmIr);
				Assert.Contains("srem i32", result.LlvmIr);
				Assert.Contains("udiv i32", result.LlvmIr);
				Assert.Contains("urem i32", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 7. Bitwise operations and shifts
	// ==========================================
	[Fact]
	public void Generate_07_BitwiseAndShifts_GeneratesMaskedShifts()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"BitwiseAndShifts",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("and i32", result.LlvmIr);
				Assert.Contains("or i32", result.LlvmIr);
				Assert.Contains("xor i32", result.LlvmIr);
				Assert.Contains("shl i32", result.LlvmIr);
				Assert.Contains("ashr i32", result.LlvmIr);
				Assert.Contains("shl i64", result.LlvmIr);
				Assert.Contains("ashr i64", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 8. Integer conversions
	// ==========================================
	[Fact]
	public void Generate_08_IntegerConversions_GeneratesSextZextTrunc()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"IntegerConversions",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("sext i32", result.LlvmIr);
				Assert.Contains("zext i32", result.LlvmIr);
				Assert.Contains("trunc i64", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 9. Comparisons
	// ==========================================
	[Fact]
	public void Generate_09_IntegerComparisons_GeneratesSignedAndUnsignedIcmp()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"IntegerComparisons",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("icmp eq i32", result.LlvmIr);
				Assert.Contains("icmp sgt i32", result.LlvmIr);
				Assert.Contains("icmp slt i32", result.LlvmIr);
				Assert.Contains("icmp ugt i32", result.LlvmIr);
				Assert.Contains("icmp ult i32", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 10. Conditional branches
	// ==========================================
	[Fact]
	public void Generate_10_SimpleIfElse_GeneratesConditionalBranches()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"SimpleIfElse",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("br i1", result.LlvmIr);
				Assert.Contains("label %block_", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 11. Loops and backedges
	// ==========================================
	[Fact]
	public void Generate_11_SumLoop_GeneratesValidLoopStructure()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"SumLoop",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("entry:", result.LlvmIr);
				Assert.Contains("br label %block_0", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 12. Multiple predecessors and stack merge (phi)
	// ==========================================
	[Fact]
	public void Generate_12_TernaryCondition_GeneratesPhiNode()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"TernaryCondition",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("phi i32", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 13. Multiple return paths
	// ==========================================
	[Fact]
	public void Generate_13_MultipleReturns_GeneratesMultipleRets()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("MultiRetTest", new Version(1, 0)),
			"MultiRetTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"MultiRet",
			MethodAttributes.Public | MethodAttributes.Static,
			module.TypeSystem.Int32
		);
		method.Parameters.Add(
			new ParameterDefinition("a", ParameterAttributes.None, module.TypeSystem.Int32)
		);
		module.Types.Add(
			new TypeDefinition(
				"TestNs",
				"TestType",
				TypeAttributes.Public,
				module.TypeSystem.Object
			)
			{
				Methods = { method },
			}
		);

		var il = method.Body.Instructions;
		var targetInstr = Instruction.Create(OpCodes.Ldc_I4_2);
		il.Add(Instruction.Create(OpCodes.Ldarg_0));
		il.Add(Instruction.Create(OpCodes.Brtrue, targetInstr));
		il.Add(Instruction.Create(OpCodes.Ldc_I4_1));
		il.Add(Instruction.Create(OpCodes.Ret));
		il.Add(targetInstr);
		il.Add(Instruction.Create(OpCodes.Ret));

		var analysis = MethodIlAnalyzer.Analyze(method);
		Assert.True(analysis.IsSupported);

		var result = LlvmIrGenerator.Generate(analysis);

		Assert.True(result.IsSuccess);
		Assert.Empty(result.Diagnostics);
		Assert.NotNull(result.LlvmIr);

		var retCount = result.LlvmIr.Split("ret i32").Length - 1;
		Assert.True(retCount >= 2, $"Expected at least 2 return instructions, found {retCount}.");

		AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
	}

	// ==========================================
	// 14. Void method
	// ==========================================
	[Fact]
	public void Generate_14_VoidMethod_GeneratesRetVoid()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"VoidMethod",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.True(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.True(result.IsSuccess);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.LlvmIr);

				Assert.Contains("define void", result.LlvmIr);
				Assert.Contains("ret void", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 15. Invalid analysis result reports AXM3001
	// ==========================================
	[Fact]
	public void Generate_15_UnsupportedAnalysis_ReportsAXM3001()
	{
		WithMethod(
			"IlAnalysisNegativeSamples",
			"TryCatchMethod",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				Assert.False(analysis.IsSupported);

				var result = LlvmIrGenerator.Generate(analysis);

				Assert.False(result.IsSuccess);
				Assert.Null(result.LlvmIr);
				Assert.Contains(result.Diagnostics, d => d.Code == "AXM3001");
			}
		);
	}

	// ==========================================
	// 16. Missing CFG reports AXM3002
	// ==========================================
	[Fact]
	public void Generate_16_MissingCfg_ReportsAXM3002()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"Add",
			method =>
			{
				var emptyAnalysis = new MethodAnalysisResult(
					method,
					IlAnalysisSupportStatus.Supported,
					null,
					Array.Empty<IlAnalysisDiagnostic>()
				);

				var result = LlvmIrGenerator.Generate(emptyAnalysis);

				Assert.False(result.IsSuccess);
				Assert.Null(result.LlvmIr);
				Assert.Contains(result.Diagnostics, d => d.Code == "AXM3002");
			}
		);
	}

	// ==========================================
	// 17. Deterministic output for identical inputs
	// ==========================================
	[Fact]
	public void Generate_17_DeterministicOutput()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"ComplexArithmetic",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				var result1 = LlvmIrGenerator.Generate(analysis);
				var result2 = LlvmIrGenerator.Generate(analysis);

				Assert.True(result1.IsSuccess);
				Assert.True(result2.IsSuccess);
				Assert.Equal(result1.LlvmIr, result2.LlvmIr);
			}
		);
	}

	// ==========================================
	// 18. NativeCompiler Facade Test
	// ==========================================
	[Fact]
	public void Generate_18_NativeCompilerFacade_DelegatesSuccessfully()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"Multiply",
			method =>
			{
				var analysis = MethodIlAnalyzer.Analyze(method);
				var result = NativeCompiler.GenerateLlvmIr(analysis);

				Assert.True(result.IsSuccess);
				Assert.NotNull(result.LlvmIr);
				Assert.Contains("mul i32", result.LlvmIr);

				AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
			}
		);
	}

	// ==========================================
	// 19. Unsupported parameter type reports AXM3003
	// ==========================================
	[Fact]
	public void Generate_19_UnsupportedParamType_ReportsAXM3003()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("TypeTest", new Version(1, 0)),
			"TypeTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"BadParam",
			MethodAttributes.Public | MethodAttributes.Static,
			module.TypeSystem.Int32
		);
		// Add an object parameter
		method.Parameters.Add(
			new ParameterDefinition("o", ParameterAttributes.None, module.TypeSystem.Object)
		);
		module.Types.Add(
			new TypeDefinition(
				"TestNs",
				"TestType",
				TypeAttributes.Public,
				module.TypeSystem.Object
			)
			{
				Methods = { method },
			}
		);

		var il = method.Body.Instructions;
		il.Add(Instruction.Create(OpCodes.Ldc_I4_0));
		il.Add(Instruction.Create(OpCodes.Ret));

		var (cfg, _) = IlControlFlowGraph.BuildAndValidate(method);
		Assert.NotNull(cfg);

		// Create synthetic analysis result marked supported to test LlvmIrGenerator type check
		var syntheticResult = new MethodAnalysisResult(
			method,
			IlAnalysisSupportStatus.Supported,
			cfg,
			Array.Empty<IlAnalysisDiagnostic>()
		);

		var result = LlvmIrGenerator.Generate(syntheticResult);

		Assert.False(result.IsSuccess);
		Assert.Null(result.LlvmIr);
		Assert.Contains(
			result.Diagnostics,
			d => d.Code == "AXM3003" && d.Message.Contains("parameter")
		);
	}

	// ==========================================
	// 20. Unsupported return type reports AXM3003
	// ==========================================
	[Fact]
	public void Generate_20_UnsupportedReturnType_ReportsAXM3003()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("RetTypeTest", new Version(1, 0)),
			"RetTypeTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"BadReturn",
			MethodAttributes.Public | MethodAttributes.Static,
			module.TypeSystem.String
		);
		module.Types.Add(
			new TypeDefinition(
				"TestNs",
				"TestType",
				TypeAttributes.Public,
				module.TypeSystem.Object
			)
			{
				Methods = { method },
			}
		);

		var il = method.Body.Instructions;
		il.Add(Instruction.Create(OpCodes.Ldnull));
		il.Add(Instruction.Create(OpCodes.Ret));

		var (cfg, _) = IlControlFlowGraph.BuildAndValidate(method);
		Assert.NotNull(cfg);

		var syntheticResult = new MethodAnalysisResult(
			method,
			IlAnalysisSupportStatus.Supported,
			cfg,
			Array.Empty<IlAnalysisDiagnostic>()
		);

		var result = LlvmIrGenerator.Generate(syntheticResult);

		Assert.False(result.IsSuccess);
		Assert.Null(result.LlvmIr);
		Assert.Contains(
			result.Diagnostics,
			d => d.Code == "AXM3003" && d.Message.Contains("return type")
		);
	}

	// ==========================================
	// 21. Unsupported instruction reports AXM3004
	// ==========================================
	[Fact]
	public void Generate_21_UnsupportedInstruction_ReportsAXM3004()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("InstrTest", new Version(1, 0)),
			"InstrTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"UnsupportedInstr",
			MethodAttributes.Public | MethodAttributes.Static,
			module.TypeSystem.Void
		);
		module.Types.Add(
			new TypeDefinition(
				"TestNs",
				"TestType",
				TypeAttributes.Public,
				module.TypeSystem.Object
			)
			{
				Methods = { method },
			}
		);

		var il = method.Body.Instructions;
		il.Add(Instruction.Create(OpCodes.Cpblk)); // Unsupported opcode
		il.Add(Instruction.Create(OpCodes.Ret));

		var (cfg, _) = IlControlFlowGraph.BuildAndValidate(method);
		Assert.NotNull(cfg);

		var syntheticResult = new MethodAnalysisResult(
			method,
			IlAnalysisSupportStatus.Supported,
			cfg,
			Array.Empty<IlAnalysisDiagnostic>()
		);

		var result = LlvmIrGenerator.Generate(syntheticResult);

		Assert.False(result.IsSuccess);
		Assert.Null(result.LlvmIr);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM3004");
	}

	// ==========================================
	// 22. Stack underflow reports AXM3005
	// ==========================================
	[Fact]
	public void Generate_22_StackUnderflow_ReportsAXM3005()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("UnderflowTest", new Version(1, 0)),
			"UnderflowTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"Underflow",
			MethodAttributes.Public | MethodAttributes.Static,
			module.TypeSystem.Int32
		);
		module.Types.Add(
			new TypeDefinition(
				"TestNs",
				"TestType",
				TypeAttributes.Public,
				module.TypeSystem.Object
			)
			{
				Methods = { method },
			}
		);

		var il = method.Body.Instructions;
		il.Add(Instruction.Create(OpCodes.Add)); // Pops 2 from empty stack
		il.Add(Instruction.Create(OpCodes.Ret));

		var (cfg, _) = IlControlFlowGraph.BuildAndValidate(method);
		Assert.NotNull(cfg);

		var syntheticResult = new MethodAnalysisResult(
			method,
			IlAnalysisSupportStatus.Supported,
			cfg,
			Array.Empty<IlAnalysisDiagnostic>()
		);

		var result = LlvmIrGenerator.Generate(syntheticResult);

		Assert.False(result.IsSuccess);
		Assert.Null(result.LlvmIr);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM3005");
	}

	// ==========================================
	// 23. LlvmIrDiagnostic properties & equality
	// ==========================================
	[Fact]
	public void Generate_23_Diagnostic_EqualityAndToString()
	{
		var diag1 = new LlvmIrDiagnostic(
			"AXM3001",
			DiagnosticSeverity.Error,
			"Test error",
			"TestNamespace.Class.Method",
			0x10,
			2
		);

		var diag2 = new LlvmIrDiagnostic(
			"AXM3001",
			DiagnosticSeverity.Error,
			"Test error",
			"TestNamespace.Class.Method",
			0x10,
			2
		);

		Assert.Equal(diag1, diag2);
		Assert.Equal(diag1.GetHashCode(), diag2.GetHashCode());
		Assert.Contains("[AXM3001][Error]", diag1.ToString());
		Assert.Contains("Block 2", diag1.ToString());
		Assert.Equal(0, diag1.CompareTo(diag2));
	}

	// ==========================================
	// 24. Audit Regression: InitLocals = false
	// ==========================================
	[Fact]
	public void Regression_Audit_01_InitLocalsFalse_DoesNotZeroInitialize()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("NoInitTest", new Version(1, 0)),
			"NoInitTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"NoInitLocals",
			MethodAttributes.Public | MethodAttributes.Static,
			module.TypeSystem.Int32
		);
		module.Types.Add(
			new TypeDefinition(
				"TestNs",
				"TestType",
				TypeAttributes.Public,
				module.TypeSystem.Object
			)
			{
				Methods = { method },
			}
		);

		method.Body.InitLocals = false;
		method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));

		var il = method.Body.Instructions;
		il.Add(Instruction.Create(OpCodes.Ldc_I4_5));
		il.Add(Instruction.Create(OpCodes.Stloc_0));
		il.Add(Instruction.Create(OpCodes.Ldloc_0));
		il.Add(Instruction.Create(OpCodes.Ret));

		var analysis = MethodIlAnalyzer.Analyze(method);
		Assert.True(analysis.IsSupported);

		var result = LlvmIrGenerator.Generate(analysis);

		Assert.True(result.IsSuccess);
		Assert.NotNull(result.LlvmIr);

		// Must have alloca for local 0
		Assert.Contains("%loc_0 = alloca i32, align 4", result.LlvmIr);
		// Must NOT have zero store in entry block because InitLocals = false
		Assert.DoesNotContain("store i32 0, ptr %loc_0", result.LlvmIr);

		AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
	}

	// ==========================================
	// 25. Audit Regression: Conditional branch with same target and fallthrough
	// ==========================================
	[Fact]
	public void Regression_Audit_02_ConditionalBranch_SameTargetAndFallthrough_DoesNotDuplicateEdge()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DupEdgeTest", new Version(1, 0)),
			"DupEdgeTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"SameTargetBranch",
			MethodAttributes.Public | MethodAttributes.Static,
			module.TypeSystem.Int32
		);
		method.Parameters.Add(
			new ParameterDefinition("a", ParameterAttributes.None, module.TypeSystem.Int32)
		);
		module.Types.Add(
			new TypeDefinition(
				"TestNs",
				"TestType",
				TypeAttributes.Public,
				module.TypeSystem.Object
			)
			{
				Methods = { method },
			}
		);

		var il = method.Body.Instructions;
		var targetInstr = Instruction.Create(OpCodes.Ldc_I4, 42);
		il.Add(Instruction.Create(OpCodes.Ldarg_0));
		il.Add(Instruction.Create(OpCodes.Brtrue, targetInstr)); // target is the immediate next instruction
		il.Add(targetInstr);
		il.Add(Instruction.Create(OpCodes.Ret));

		var analysis = MethodIlAnalyzer.Analyze(method);
		Assert.True(analysis.IsSupported);

		var result = LlvmIrGenerator.Generate(analysis);

		Assert.True(result.IsSuccess);
		Assert.NotNull(result.LlvmIr);

		// Must not have dual edges to the same block
		AssertValidWithLlvmOptIfAvailable(result.LlvmIr);
	}
}

using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Axiom.Native.Compiler.Tests;

public class MethodIlAnalyzerTests
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

	// ==========================================
	// 10 Позитивных сценариев
	// ==========================================

	[Fact]
	public void Positive_01_Add_IsSupported()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"Add",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.Cfg);
			}
		);
	}

	[Fact]
	public void Positive_02_Subtract_IsSupported()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"Subtract",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
			}
		);
	}

	[Fact]
	public void Positive_03_Multiply_IsSupported()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"Multiply",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
			}
		);
	}

	[Fact]
	public void Positive_04_LocalsMethod_IsSupported()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"LocalsMethod",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
			}
		);
	}

	[Fact]
	public void Positive_05_ReturnConstant_IsSupported()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"ReturnConstant",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
			}
		);
	}

	[Fact]
	public void Positive_06_ComplexArithmetic_IsSupported()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"ComplexArithmetic",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
			}
		);
	}

	[Fact]
	public void Positive_07_SimpleIfElse_IsSupported()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"SimpleIfElse",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.Cfg);
				Assert.True(result.Cfg.Blocks.Count > 1);
			}
		);
	}

	[Fact]
	public void Positive_08_SumLoop_IsSupported()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"SumLoop",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
				Assert.NotNull(result.Cfg);
				Assert.True(result.Cfg.Blocks.Count >= 3);
			}
		);
	}

	[Fact]
	public void Positive_09_MultiBranchMerge_IsSupported()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"MultiBranchMerge",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
			}
		);
	}

	[Fact]
	public void Positive_10_VoidMethod_IsSupported()
	{
		WithMethod(
			"IlAnalysisPositiveSamples",
			"VoidMethod",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
			}
		);
	}

	// ==========================================
	// 10 Негативных сценариев
	// ==========================================

	[Fact]
	public void Negative_01_UnsupportedInstruction_Switch_ReportsAXM2001()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"SwitchMethod",
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
		var retInstr = Instruction.Create(OpCodes.Ret);
		il.Add(Instruction.Create(OpCodes.Ldc_I4_0));
		il.Add(Instruction.Create(OpCodes.Switch, new[] { retInstr }));
		il.Add(retInstr);

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.UnsupportedInstruction, result.SupportStatus);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2001");
	}

	[Fact]
	public void Negative_02_IncompatibleOperandTypes_ReportsAXM2005()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"MismatchMethod",
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
		il.Add(Instruction.Create(OpCodes.Ldc_I4_1));
		il.Add(Instruction.Create(OpCodes.Ldc_I8, 2L));
		il.Add(Instruction.Create(OpCodes.Add)); // Mismatch: I4 and I8
		il.Add(Instruction.Create(OpCodes.Ret));

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.InvalidIlStack, result.SupportStatus);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2005");
	}

	[Fact]
	public void Negative_03_StackUnderflow_ReportsAXM2004()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"UnderflowMethod",
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
		il.Add(Instruction.Create(OpCodes.Pop)); // Underflow on empty stack
		il.Add(Instruction.Create(OpCodes.Ret));

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.InvalidIlStack, result.SupportStatus);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2004");
	}

	[Fact]
	public void Negative_04_InvalidReturn_NonEmptyStackForVoid_ReportsAXM2008()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"BadRetMethod",
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
		il.Add(Instruction.Create(OpCodes.Ldc_I4_5)); // Leaves item on stack in void method
		il.Add(Instruction.Create(OpCodes.Ret));

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.InvalidControlFlow, result.SupportStatus);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2008");
	}

	[Fact]
	public void Negative_05_InvalidBranchTarget_OutsideMethod_ReportsAXM2007()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"BadBranchMethod",
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

		var foreignInstr = Instruction.Create(OpCodes.Nop); // Instruction not added to method
		var il = method.Body.Instructions;
		il.Add(Instruction.Create(OpCodes.Br, foreignInstr));
		il.Add(Instruction.Create(OpCodes.Ret));

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.InvalidControlFlow, result.SupportStatus);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2007");
	}

	[Fact]
	public void Negative_06_StackMergeMismatch_ReportsAXM2006()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"MergeMismatchMethod",
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
		var mergeTarget = Instruction.Create(OpCodes.Ret);

		il.Add(Instruction.Create(OpCodes.Ldc_I4_1));
		il.Add(Instruction.Create(OpCodes.Brtrue, mergeTarget)); // Branch leaves empty stack!

		il.Add(Instruction.Create(OpCodes.Ldc_I4_2)); // Fallthrough pushes 1 item!
		il.Add(mergeTarget); // At mergeTarget: branch had 0 items, fallthrough had 1 item!

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.InvalidIlStack, result.SupportStatus);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2006");
	}

	[Fact]
	public void Negative_07_TryCatchRegion_ReportsAXM2003()
	{
		WithMethod(
			"IlAnalysisNegativeSamples",
			"TryCatchMethod",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.False(result.IsSupported);
				Assert.Equal(
					IlAnalysisSupportStatus.UnsupportedExceptionHandling,
					result.SupportStatus
				);
				Assert.Contains(result.Diagnostics, d => d.Code == "AXM2003");
			}
		);
	}

	[Fact]
	public void Negative_08_GenericMethod_ReportsAXM2002()
	{
		WithMethod(
			"IlAnalysisNegativeSamples",
			"GenericMethod",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.False(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.UnsupportedType, result.SupportStatus);
				Assert.Contains(result.Diagnostics, d => d.Code == "AXM2002");
			}
		);
	}

	[Fact]
	public void Negative_09_UnsupportedSignature_Double_ReportsAXM2002()
	{
		WithMethod(
			"IlAnalysisNegativeSamples",
			"UnsupportedSignatureMethod",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.False(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.UnsupportedType, result.SupportStatus);
				Assert.Contains(result.Diagnostics, d => d.Code == "AXM2002");
			}
		);
	}

	[Fact]
	public void Negative_10_MethodCall_ReportsAXM2011()
	{
		WithMethod(
			"CallChainSample",
			"RootChain",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.False(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.UnsupportedMethodCall, result.SupportStatus);
				Assert.Contains(result.Diagnostics, d => d.Code == "AXM2011");
			}
		);
	}

	// ==========================================
	// Интеграция с MethodDependencyGraph
	// ==========================================

	[Fact]
	public void Integration_AnalyzeGraph_AnalyzesAllNodesDeterministically()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);
		var graph = MethodDependencyResolver.Resolve(context);

		var results = MethodIlAnalyzer.Analyze(graph);

		Assert.NotEmpty(results);
		Assert.Equal(graph.Nodes.Count, results.Count);

		// Pure math roots without calls should be Supported
		var addKey = graph.Roots.First(r => r.Reference.Name == "Add").Key;
		Assert.True(results[addKey].IsSupported);

		// Roots with calls should be UnsupportedMethodCall (AXM2011)
		var chainKey = graph.Roots.First(r => r.Reference.Name == "RootChain").Key;
		Assert.Equal(
			IlAnalysisSupportStatus.UnsupportedMethodCall,
			results[chainKey].SupportStatus
		);
	}

	// ==========================================
	// Регрессионные тесты
	// ==========================================

	[Fact]
	public void Regression_InstanceMethod_WithI4AndI8_IsSupported()
	{
		WithMethod(
			"InstanceArgsSample",
			"AddInstanceArgs",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
			}
		);
	}

	[Fact]
	public void Regression_StaticMethod_WithI4AndI8_IsSupported()
	{
		WithMethod(
			"StaticArgsSample",
			"AddStaticArgs",
			method =>
			{
				var result = MethodIlAnalyzer.Analyze(method);

				Assert.True(result.IsSupported);
				Assert.Equal(IlAnalysisSupportStatus.Supported, result.SupportStatus);
				Assert.Empty(result.Diagnostics);
			}
		);
	}

	[Fact]
	public void Regression_InstanceMethod_AccessingThis_ReportsAXM2002()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var type = new TypeDefinition(
			"TestNs",
			"TestClass",
			TypeAttributes.Public | TypeAttributes.Class,
			module.TypeSystem.Object
		);
		module.Types.Add(type);

		// Instance method with this
		var method = new MethodDefinition(
			"InstanceThisAccess",
			MethodAttributes.Public | MethodAttributes.Virtual,
			module.TypeSystem.Void
		);
		type.Methods.Add(method);

		var il = method.Body.Instructions;
		il.Add(Instruction.Create(OpCodes.Ldarg_0)); // Accesses 'this'
		il.Add(Instruction.Create(OpCodes.Pop));
		il.Add(Instruction.Create(OpCodes.Ret));

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.UnsupportedType, result.SupportStatus);
		Assert.Contains(
			result.Diagnostics,
			d => d.Code == "AXM2002" && d.Message.Contains("'this'")
		);
	}

	[Fact]
	public void Regression_InvalidArgIndex_DoesNotPolluteStack()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		// Static method with 0 parameters
		var method = new MethodDefinition(
			"BadArgMethod",
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
		il.Add(Instruction.Create(OpCodes.Ldarg_1)); // Out of bounds: accesses arg 1 when total parameters is 0
		il.Add(Instruction.Create(OpCodes.Ret));

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.InvalidMetadata, result.SupportStatus);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2009");
		// Ensure no fake I4 was pushed that would cause false AXM2008 (non-empty stack at ret)
		Assert.DoesNotContain(result.Diagnostics, d => d.Code == "AXM2008");
	}

	[Fact]
	public void Regression_InvalidLocIndex_DoesNotPolluteStack()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		// Method with 0 local variables
		var method = new MethodDefinition(
			"BadLocMethod",
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
		il.Add(Instruction.Create(OpCodes.Ldloc_1)); // Out of bounds: accesses local 1 when total locals is 0
		il.Add(Instruction.Create(OpCodes.Ret));

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.InvalidMetadata, result.SupportStatus);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2009");
		// Ensure no fake I4 was pushed that would cause false AXM2008
		Assert.DoesNotContain(result.Diagnostics, d => d.Code == "AXM2008");
	}

	[Fact]
	public void Regression_LoopBackEdgeConflict_ReportsAXM2006()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"LoopConflictMethod",
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
		var loopHeader = Instruction.Create(OpCodes.Nop);
		var loopBody = Instruction.Create(OpCodes.Nop);
		var exit = Instruction.Create(OpCodes.Ret);

		// Entry -> loopHeader (entry stack has 0 items)
		il.Add(Instruction.Create(OpCodes.Br, loopHeader));

		// LoopHeader
		il.Add(loopHeader);
		il.Add(Instruction.Create(OpCodes.Ldc_I4_1));
		il.Add(Instruction.Create(OpCodes.Brtrue, loopBody));
		il.Add(Instruction.Create(OpCodes.Br, exit));

		// LoopBody: pushes I4 onto stack and branches back to loopHeader!
		// LoopHeader had 0 items, but back-edge brings 1 item -> Conflict!
		il.Add(loopBody);
		il.Add(Instruction.Create(OpCodes.Ldc_I4_5));
		il.Add(Instruction.Create(OpCodes.Br, loopHeader));

		il.Add(exit);

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.InvalidIlStack, result.SupportStatus);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2006");
	}

	[Fact]
	public void Regression_MergePointIncompatibleStacks_ReportsAXM2006()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"TypeMergeConflictMethod",
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
		var pathA = Instruction.Create(OpCodes.Ldc_I4_1);
		var pathB = Instruction.Create(OpCodes.Ldc_I8, 10L);
		var mergeTarget = Instruction.Create(OpCodes.Pop);

		il.Add(Instruction.Create(OpCodes.Ldc_I4_0));
		il.Add(Instruction.Create(OpCodes.Brtrue, pathA));

		// Path B: pushes I8 to merge
		il.Add(pathB);
		il.Add(Instruction.Create(OpCodes.Br, mergeTarget));

		// Path A: pushes I4 to merge
		il.Add(pathA);
		il.Add(Instruction.Create(OpCodes.Br, mergeTarget));

		il.Add(mergeTarget);
		il.Add(Instruction.Create(OpCodes.Ret));

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.InvalidIlStack, result.SupportStatus);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2006");
	}

	[Fact]
	public void Regression_UnresolvedDependencyNode_ReportsAXM2010WithoutException()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var unresolvableRef = new MethodReference(
			"MissingMethod",
			module.TypeSystem.Void,
			new TypeReference(
				"NonExistentNs",
				"NonExistentType",
				module,
				new AssemblyNameReference("MissingAsm", new Version(1, 0))
			)
		);

		var node = new MethodDependencyNode(
			unresolvableRef.FullName,
			unresolvableRef,
			definition: null,
			isRoot: false,
			explicitOptimizeFor: null,
			supportStatus: DependencySupportStatus.UnsupportedUnresolvedReference
		);

		// Must not throw InvalidOperationException or call Resolve()
		var result = MethodIlAnalyzer.Analyze(node);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.InvalidMetadata, result.SupportStatus);
		Assert.Null(result.Method);
		Assert.NotNull(result.Reference);
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2010");
	}

	[Fact]
	public void Regression_UnsupportedLocalType_DoesNotEmitFalseAXM2005()
	{
		using var assembly = AssemblyDefinition.CreateAssembly(
			new AssemblyNameDefinition("DynTest", new Version(1, 0)),
			"DynTest.dll",
			ModuleKind.Dll
		);
		var module = assembly.MainModule;
		var method = new MethodDefinition(
			"UnsupportedLocalMethod",
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

		// Local variable of unsupported type 'Double'
		method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Double));

		var il = method.Body.Instructions;
		il.Add(Instruction.Create(OpCodes.Ldc_I4_1));
		il.Add(Instruction.Create(OpCodes.Stloc_0)); // Storing into Double local
		il.Add(Instruction.Create(OpCodes.Ret));

		var result = MethodIlAnalyzer.Analyze(method);

		Assert.False(result.IsSupported);
		Assert.Equal(IlAnalysisSupportStatus.UnsupportedType, result.SupportStatus);
		// Local type is unsupported -> AXM2002
		Assert.Contains(result.Diagnostics, d => d.Code == "AXM2002");
		// Ensure NO false AXM2005 was emitted
		Assert.DoesNotContain(result.Diagnostics, d => d.Code == "AXM2005");
	}
}

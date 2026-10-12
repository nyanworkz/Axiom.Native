using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Axiom.Native.Compiler.Tests;

public class NativeAssemblyLoaderTests
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

	[Fact]
	public void Load_NonExistentFile_ThrowsFileNotFoundException()
	{
		var nonExistentPath = Path.Combine(AppContext.BaseDirectory, "NonExistentAssembly.dll");

		Assert.Throws<FileNotFoundException>(() =>
		{
			using var context = NativeAssemblyLoader.Load(nonExistentPath);
		});
	}

	[Fact]
	public void Load_ValidAssembly_LoadsSuccessfullyAndDisposesCleanly()
	{
		var path = GetTestAssemblyPath();

		using (var context = NativeAssemblyLoader.Load(path))
		{
			Assert.NotNull(context.Assembly);
			Assert.Equal("Axiom.Native.Compiler.Tests.TestAssembly", context.Assembly.Name.Name);
		}
	}

	[Fact]
	public void Discover_FindsExplicitlyMarkedMethods()
	{
		var path = GetTestAssemblyPath();

		using var context = NativeAssemblyLoader.Load(path);
		var result = NativeAssemblyLoader.Discover(context);

		Assert.False(result.HasErrors);
		Assert.Empty(result.Diagnostics);

		var addMethod = result.Methods.FirstOrDefault(m => m.Method.Name == "Add");
		Assert.NotNull(addMethod);
		Assert.Equal(OptimizeFor.ExecutionSpeed, addMethod.OptimizeFor);
		Assert.False(addMethod.IsInheritedFromStruct);

		var subtractMethod = result.Methods.FirstOrDefault(m => m.Method.Name == "Subtract");
		Assert.NotNull(subtractMethod);
		Assert.Equal(OptimizeFor.Debug, subtractMethod.OptimizeFor);
		Assert.False(subtractMethod.IsInheritedFromStruct);

		var multiplyMethod = result.Methods.FirstOrDefault(m => m.Method.Name == "Multiply");
		Assert.NotNull(multiplyMethod);
		Assert.Equal(OptimizeFor.BuildSize, multiplyMethod.OptimizeFor);
		Assert.False(multiplyMethod.IsInheritedFromStruct);

		var unmarkedMethod = result.Methods.FirstOrDefault(m => m.Method.Name == "UnmarkedMethod");
		Assert.Null(unmarkedMethod);
	}

	[Fact]
	public void Discover_FindsMethodsInNestedClasses()
	{
		var path = GetTestAssemblyPath();

		using var context = NativeAssemblyLoader.Load(path);
		var result = NativeAssemblyLoader.Discover(context);

		var deepNestedMethod = result.Methods.FirstOrDefault(m =>
			m.Method.Name == "DeepNestedMethod"
		);
		Assert.NotNull(deepNestedMethod);
		Assert.Equal(OptimizeFor.ExecutionSpeed, deepNestedMethod.OptimizeFor);
		Assert.False(deepNestedMethod.IsInheritedFromStruct);
	}

	[Fact]
	public void Discover_FindsStructsAndHandlesInheritedMethods()
	{
		var path = GetTestAssemblyPath();

		using var context = NativeAssemblyLoader.Load(path);
		var result = NativeAssemblyLoader.Discover(context);

		var nativePointStruct = result.Structs.FirstOrDefault(s => s.Type.Name == "NativePoint");
		Assert.NotNull(nativePointStruct);
		Assert.Equal(OptimizeFor.BuildSize, nativePointStruct.OptimizeFor);

		var getNormSquaredMethod = result.Methods.FirstOrDefault(m =>
			m.Method.DeclaringType.Name == "NativePoint" && m.Method.Name == "GetNormSquared"
		);
		Assert.NotNull(getNormSquaredMethod);
		Assert.Equal(OptimizeFor.BuildSize, getNormSquaredMethod.OptimizeFor);
		Assert.True(getNormSquaredMethod.IsInheritedFromStruct);

		var fastResetMethod = result.Methods.FirstOrDefault(m =>
			m.Method.DeclaringType.Name == "NativePoint" && m.Method.Name == "FastReset"
		);
		Assert.NotNull(fastResetMethod);
		Assert.Equal(OptimizeFor.ExecutionSpeed, fastResetMethod.OptimizeFor);
		Assert.False(fastResetMethod.IsInheritedFromStruct);
	}

	[Fact]
	public void Discover_FindsNestedStructsAndTheirMethods()
	{
		var path = GetTestAssemblyPath();

		using var context = NativeAssemblyLoader.Load(path);
		var result = NativeAssemblyLoader.Discover(context);

		var nestedStruct = result.Structs.FirstOrDefault(s => s.Type.Name == "NestedStruct");
		Assert.NotNull(nestedStruct);
		Assert.Equal(OptimizeFor.ExecutionSpeed, nestedStruct.OptimizeFor);

		var getValueMethod = result.Methods.FirstOrDefault(m =>
			m.Method.DeclaringType.Name == "NestedStruct" && m.Method.Name == "GetValue"
		);
		Assert.NotNull(getValueMethod);
		Assert.Equal(OptimizeFor.ExecutionSpeed, getValueMethod.OptimizeFor);
		Assert.True(getValueMethod.IsInheritedFromStruct);
	}

	[Fact]
	public void Discover_MethodWithoutBody_ReportsDiagnosticError()
	{
		var assemblyName = new Mono.Cecil.AssemblyNameDefinition("TestDyn1", new Version(1, 0));
		using var assembly = Mono.Cecil.AssemblyDefinition.CreateAssembly(
			assemblyName,
			"TestDyn1.dll",
			Mono.Cecil.ModuleKind.Dll
		);
		var module = assembly.MainModule;

		var type = new Mono.Cecil.TypeDefinition(
			"TestNs",
			"TestClass",
			Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract,
			module.TypeSystem.Object
		);
		module.Types.Add(type);

		var method = new Mono.Cecil.MethodDefinition(
			"AbstractMethod",
			Mono.Cecil.MethodAttributes.Public
				| Mono.Cecil.MethodAttributes.Abstract
				| Mono.Cecil.MethodAttributes.Virtual,
			module.TypeSystem.Void
		);
		type.Methods.Add(method);

		var attrType = new Mono.Cecil.TypeReference(
			"Axiom.Native",
			"NativeCompileAttribute",
			module,
			null
		);
		var attrCtor = new Mono.Cecil.MethodReference(".ctor", module.TypeSystem.Void, attrType)
		{
			HasThis = true,
		};
		method.CustomAttributes.Add(new Mono.Cecil.CustomAttribute(attrCtor));

		var result = NativeAssemblyLoader.Discover(assembly);

		Assert.True(result.HasErrors);
		Assert.Contains(
			result.Diagnostics,
			d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("abstract")
		);
		Assert.Empty(result.Methods);
	}

	[Fact]
	public void Discover_ClassWithNativeCompile_ReportsDiagnosticError()
	{
		var assemblyName = new Mono.Cecil.AssemblyNameDefinition("TestDyn2", new Version(1, 0));
		using var assembly = Mono.Cecil.AssemblyDefinition.CreateAssembly(
			assemblyName,
			"TestDyn2.dll",
			Mono.Cecil.ModuleKind.Dll
		);
		var module = assembly.MainModule;

		var type = new Mono.Cecil.TypeDefinition(
			"TestNs",
			"InvalidClass",
			Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
			module.TypeSystem.Object
		);
		module.Types.Add(type);

		var attrType = new Mono.Cecil.TypeReference(
			"Axiom.Native",
			"NativeCompileAttribute",
			module,
			null
		);
		var attrCtor = new Mono.Cecil.MethodReference(".ctor", module.TypeSystem.Void, attrType)
		{
			HasThis = true,
		};
		type.CustomAttributes.Add(new Mono.Cecil.CustomAttribute(attrCtor));

		var result = NativeAssemblyLoader.Discover(assembly);

		Assert.True(result.HasErrors);
		Assert.Contains(
			result.Diagnostics,
			d =>
				d.Severity == DiagnosticSeverity.Error
				&& d.Message.Contains("only valid on structs")
		);
	}

	[Fact]
	public void Discover_InvalidOptimizeForValue_ReportsDiagnosticError()
	{
		var assemblyName = new Mono.Cecil.AssemblyNameDefinition("TestDyn3", new Version(1, 0));
		using var assembly = Mono.Cecil.AssemblyDefinition.CreateAssembly(
			assemblyName,
			"TestDyn3.dll",
			Mono.Cecil.ModuleKind.Dll
		);
		var module = assembly.MainModule;

		var type = new Mono.Cecil.TypeDefinition(
			"TestNs",
			"TestClass",
			Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
			module.TypeSystem.Object
		);
		module.Types.Add(type);

		var method = new Mono.Cecil.MethodDefinition(
			"ValidBodyMethod",
			Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
			module.TypeSystem.Void
		);
		method.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
		type.Methods.Add(method);

		var attrType = new Mono.Cecil.TypeReference(
			"Axiom.Native",
			"NativeCompileAttribute",
			module,
			null
		);
		var attrCtor = new Mono.Cecil.MethodReference(".ctor", module.TypeSystem.Void, attrType)
		{
			HasThis = true,
		};
		var customAttr = new Mono.Cecil.CustomAttribute(attrCtor);
		customAttr.ConstructorArguments.Add(
			new Mono.Cecil.CustomAttributeArgument(module.TypeSystem.Int32, 9999)
		);
		method.CustomAttributes.Add(customAttr);

		var result = NativeAssemblyLoader.Discover(assembly);

		Assert.True(result.HasErrors);
		Assert.Contains(
			result.Diagnostics,
			d =>
				d.Severity == DiagnosticSeverity.Error
				&& d.Message.Contains("Invalid OptimizeFor value '9999'")
		);
	}
}

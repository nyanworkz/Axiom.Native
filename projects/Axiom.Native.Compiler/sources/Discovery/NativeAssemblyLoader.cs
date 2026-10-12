using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace Axiom.Native;

public static class NativeAssemblyLoader
{
	private const string NativeCompileAttributeFullName = "Axiom.Native.NativeCompileAttribute";

	public static NativeAssemblyContext Load(string assemblyPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

		if (!File.Exists(assemblyPath))
		{
			throw new FileNotFoundException(
				$"Assembly not found at path: '{assemblyPath}'",
				assemblyPath
			);
		}

		var directory =
			Path.GetDirectoryName(Path.GetFullPath(assemblyPath)) ?? AppContext.BaseDirectory;

		var resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(directory);

		var readerParameters = new ReaderParameters
		{
			AssemblyResolver = resolver,
			ReadingMode = ReadingMode.Immediate,
			InMemory = true,
			ReadSymbols = false,
		};

		var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
		return new NativeAssemblyContext(assembly, resolver);
	}

	public static NativeAssemblyContext Load(Stream stream, string? searchDirectory = null)
	{
		ArgumentNullException.ThrowIfNull(stream);

		var resolver = new DefaultAssemblyResolver();
		if (!string.IsNullOrWhiteSpace(searchDirectory) && Directory.Exists(searchDirectory))
		{
			resolver.AddSearchDirectory(searchDirectory);
		}

		var readerParameters = new ReaderParameters
		{
			AssemblyResolver = resolver,
			ReadingMode = ReadingMode.Immediate,
			InMemory = true,
			ReadSymbols = false,
		};

		var assembly = AssemblyDefinition.ReadAssembly(stream, readerParameters);
		return new NativeAssemblyContext(assembly, resolver);
	}

	public static AssemblyDiscoveryResult Discover(NativeAssemblyContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		return Discover(context.Assembly);
	}

	public static AssemblyDiscoveryResult Discover(AssemblyDefinition assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);

		var discoveredMethods = new List<DiscoveredMethod>();
		var discoveredStructs = new List<DiscoveredStruct>();
		var diagnostics = new List<DiscoveryDiagnostic>();

		foreach (var module in assembly.Modules)
		{
			foreach (var type in EnumerateAllTypes(module))
			{
				OptimizeFor? structOptimizeFor = null;
				var typeAttribute = FindNativeCompileAttribute(type);

				if (typeAttribute is not null)
				{
					if (type.IsValueType && !type.IsEnum)
					{
						if (TryExtractOptimizeFor(typeAttribute, out var opt, out var optError))
						{
							structOptimizeFor = opt;
							discoveredStructs.Add(new DiscoveredStruct(type, opt));
						}
						else
						{
							diagnostics.Add(
								new DiscoveryDiagnostic(
									DiagnosticSeverity.Error,
									optError!,
									type.FullName
								)
							);
						}
					}
					else
					{
						diagnostics.Add(
							new DiscoveryDiagnostic(
								DiagnosticSeverity.Error,
								"[NativeCompile] attribute is only valid on structs and methods.",
								type.FullName
							)
						);
					}
				}

				if (!type.HasMethods)
				{
					continue;
				}

				foreach (var method in type.Methods)
				{
					var methodAttribute = FindNativeCompileAttribute(method);

					if (methodAttribute is not null)
					{
						if (
							TryExtractOptimizeFor(
								methodAttribute,
								out var methodOpt,
								out var methodOptError
							)
						)
						{
							ValidateAndAddMethod(
								method,
								methodOpt,
								isInheritedFromStruct: false,
								discoveredMethods,
								diagnostics
							);
						}
						else
						{
							diagnostics.Add(
								new DiscoveryDiagnostic(
									DiagnosticSeverity.Error,
									methodOptError!,
									method.FullName
								)
							);
						}
					}
					else if (structOptimizeFor.HasValue)
					{
						ValidateAndAddMethod(
							method,
							structOptimizeFor.Value,
							isInheritedFromStruct: true,
							discoveredMethods,
							diagnostics
						);
					}
				}
			}
		}

		return new AssemblyDiscoveryResult(discoveredMethods, discoveredStructs, diagnostics);
	}

	private static void ValidateAndAddMethod(
		MethodDefinition method,
		OptimizeFor optimizeFor,
		bool isInheritedFromStruct,
		List<DiscoveredMethod> methods,
		List<DiscoveryDiagnostic> diagnostics
	)
	{
		if (method.IsAbstract || method.IsPInvokeImpl || !method.HasBody)
		{
			var reason =
				method.IsAbstract ? "abstract"
				: method.IsPInvokeImpl ? "P/Invoke"
				: "without IL body";

			diagnostics.Add(
				new DiscoveryDiagnostic(
					DiagnosticSeverity.Error,
					$"Method is targeted for native compilation but is {reason}.",
					method.FullName
				)
			);
			return;
		}

		methods.Add(new DiscoveredMethod(method, optimizeFor, isInheritedFromStruct));
	}

	private static IEnumerable<TypeDefinition> EnumerateAllTypes(ModuleDefinition module)
	{
		foreach (var type in module.Types)
		{
			foreach (var nested in EnumerateTypeAndNested(type))
			{
				yield return nested;
			}
		}
	}

	private static IEnumerable<TypeDefinition> EnumerateTypeAndNested(TypeDefinition type)
	{
		yield return type;

		if (type.HasNestedTypes)
		{
			foreach (var nested in type.NestedTypes)
			{
				foreach (var item in EnumerateTypeAndNested(nested))
				{
					yield return item;
				}
			}
		}
	}

	private static CustomAttribute? FindNativeCompileAttribute(ICustomAttributeProvider provider)
	{
		if (!provider.HasCustomAttributes)
		{
			return null;
		}

		foreach (var attribute in provider.CustomAttributes)
		{
			if (attribute.AttributeType.FullName == NativeCompileAttributeFullName)
			{
				return attribute;
			}
		}

		return null;
	}

	private static bool TryExtractOptimizeFor(
		CustomAttribute attribute,
		out OptimizeFor optimizeFor,
		out string? errorMessage
	)
	{
		optimizeFor = OptimizeFor.ExecutionSpeed;
		errorMessage = null;

		if (!attribute.HasConstructorArguments || attribute.ConstructorArguments.Count == 0)
		{
			return true;
		}

		var argument = attribute.ConstructorArguments[0];
		if (argument.Value is IConvertible convertible)
		{
			try
			{
				var intVal = Convert.ToInt32(convertible);
				if (Enum.IsDefined(typeof(OptimizeFor), intVal))
				{
					optimizeFor = (OptimizeFor)intVal;
					return true;
				}

				errorMessage = $"Invalid OptimizeFor value '{intVal}'.";
				return false;
			}
			catch (Exception ex)
			{
				errorMessage = $"Failed to convert OptimizeFor argument: {ex.Message}";
				return false;
			}
		}

		errorMessage =
			$"Unexpected argument type for OptimizeFor: {argument.Value?.GetType().FullName ?? "null"}";
		return false;
	}
}

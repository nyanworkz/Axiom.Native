using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Axiom.Native;

public static class MethodDependencyResolver
{
	public static MethodDependencyGraph Resolve(NativeAssemblyContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var discoveryResult = NativeAssemblyLoader.Discover(context);
		return Resolve(discoveryResult, context.Assembly);
	}

	public static MethodDependencyGraph Resolve(
		AssemblyDiscoveryResult discoveryResult,
		AssemblyDefinition? rootAssembly = null
	)
	{
		ArgumentNullException.ThrowIfNull(discoveryResult);

		var nodesByKey = new Dictionary<string, MethodDependencyNode>(StringComparer.Ordinal);
		var rootsList = new List<MethodDependencyNode>();
		var workQueue = new Queue<MethodDependencyNode>();
		var visitedForAnalysis = new HashSet<string>(StringComparer.Ordinal);
		var diagnosticsSet = new HashSet<DependencyDiagnostic>();

		foreach (var discoveredMethod in discoveryResult.Methods)
		{
			var key = discoveredMethod.Method.FullName;
			if (!nodesByKey.TryGetValue(key, out var rootNode))
			{
				rootNode = new MethodDependencyNode(
					key,
					discoveredMethod.Method,
					discoveredMethod.Method,
					isRoot: true,
					explicitOptimizeFor: discoveredMethod.OptimizeFor,
					supportStatus: DependencySupportStatus.Supported,
					unsupportedReason: null,
					isExternal: false
				);

				nodesByKey[key] = rootNode;
				rootsList.Add(rootNode);
				workQueue.Enqueue(rootNode);
			}
		}

		while (workQueue.Count > 0)
		{
			var callerNode = workQueue.Dequeue();

			if (!visitedForAnalysis.Add(callerNode.Key))
			{
				continue;
			}

			if (callerNode.Definition is null || !callerNode.Definition.HasBody)
			{
				continue;
			}

			if (callerNode.IsExternal && IsFrameworkAssembly(callerNode.Definition.Module.Assembly))
			{
				continue;
			}

			foreach (var instruction in callerNode.Definition.Body.Instructions)
			{
				DependencyCallKind callKind;
				if (instruction.OpCode == OpCodes.Call)
				{
					callKind = DependencyCallKind.Call;
				}
				else if (instruction.OpCode == OpCodes.Callvirt)
				{
					callKind = DependencyCallKind.CallVirt;
				}
				else
				{
					continue;
				}

				if (instruction.Operand is not MethodReference callSiteRef)
				{
					continue;
				}

				MethodDefinition? targetDef = null;
				try
				{
					targetDef = callSiteRef.Resolve();
				}
				catch (Exception ex)
				{
					diagnosticsSet.Add(
						new DependencyDiagnostic(
							"AXM1001",
							DiagnosticSeverity.Error,
							$"Failed to resolve method reference '{callSiteRef.FullName}': {ex.Message}",
							callerNode.Key,
							callSiteRef.FullName,
							instruction.Offset
						)
					);
				}

				var targetKey = callSiteRef.FullName;

				if (!nodesByKey.TryGetValue(targetKey, out var targetNode))
				{
					var (supportStatus, unsupportedReason, diagnostic) = EvaluateSupportStatus(
						callSiteRef,
						targetDef,
						callKind,
						callerNode.Key,
						instruction.Offset
					);

					if (diagnostic is not null)
					{
						diagnosticsSet.Add(diagnostic);
					}

					var isExternal =
						targetDef is not null
						&& rootAssembly is not null
						&& targetDef.Module.Assembly.Name.FullName != rootAssembly.Name.FullName;

					targetNode = new MethodDependencyNode(
						targetKey,
						callSiteRef,
						targetDef,
						isRoot: false,
						explicitOptimizeFor: null,
						supportStatus: supportStatus,
						unsupportedReason: unsupportedReason,
						isExternal: isExternal
					);

					nodesByKey[targetKey] = targetNode;

					if (targetNode.IsSupported && targetDef is not null && targetDef.HasBody)
					{
						workQueue.Enqueue(targetNode);
					}
				}
				else
				{
					var callSiteDiagnostic = CheckCallSiteDispatch(
						callSiteRef,
						targetNode.Definition,
						callKind,
						callerNode.Key,
						instruction.Offset
					);

					if (callSiteDiagnostic is not null)
					{
						diagnosticsSet.Add(callSiteDiagnostic);
					}
				}

				var edge = new MethodDependencyEdge(
					callerNode,
					targetNode,
					callKind,
					instruction.Offset,
					callSiteRef
				);

				callerNode.AddOutgoingCall(edge);
				targetNode.AddIncomingCall(edge);
			}
		}

		var sortedDiagnostics = diagnosticsSet.OrderBy(d => d).ToList();
		return new MethodDependencyGraph(rootsList, nodesByKey, sortedDiagnostics);
	}

	private static (
		DependencySupportStatus Status,
		string? Reason,
		DependencyDiagnostic? Diagnostic
	) EvaluateSupportStatus(
		MethodReference callSiteRef,
		MethodDefinition? targetDef,
		DependencyCallKind callKind,
		string callerKey,
		int ilOffset
	)
	{
		if (targetDef is null)
		{
			var diagnostic = new DependencyDiagnostic(
				"AXM1001",
				DiagnosticSeverity.Error,
				$"Failed to resolve metadata reference '{callSiteRef.FullName}'.",
				callerKey,
				callSiteRef.FullName,
				ilOffset
			);
			return (
				DependencySupportStatus.UnsupportedUnresolvedReference,
				"Unresolved reference",
				diagnostic
			);
		}

		if (
			callSiteRef is GenericInstanceMethod
			|| callSiteRef.DeclaringType is GenericInstanceType
			|| callSiteRef.HasGenericParameters
			|| targetDef.HasGenericParameters
		)
		{
			var diagnostic = new DependencyDiagnostic(
				"AXM1006",
				DiagnosticSeverity.Error,
				$"Generic method or type call '{callSiteRef.FullName}' is not yet supported for native compilation.",
				callerKey,
				callSiteRef.FullName,
				ilOffset
			);
			return (
				DependencySupportStatus.UnsupportedGenericSpecialization,
				"Generic specialization not supported",
				diagnostic
			);
		}

		var dynamicDispatchDiag = CheckCallSiteDispatch(
			callSiteRef,
			targetDef,
			callKind,
			callerKey,
			ilOffset
		);
		if (dynamicDispatchDiag is not null)
		{
			return (
				DependencySupportStatus.UnsupportedDynamicDispatch,
				"Dynamic dispatch not supported",
				dynamicDispatchDiag
			);
		}

		if (targetDef.IsAbstract)
		{
			var diagnostic = new DependencyDiagnostic(
				"AXM1002",
				DiagnosticSeverity.Error,
				$"Target method '{targetDef.FullName}' is abstract and has no IL body.",
				callerKey,
				callSiteRef.FullName,
				ilOffset
			);
			return (
				DependencySupportStatus.UnsupportedNoBodyAbstract,
				"Abstract method",
				diagnostic
			);
		}

		if (targetDef.IsPInvokeImpl)
		{
			var diagnostic = new DependencyDiagnostic(
				"AXM1003",
				DiagnosticSeverity.Error,
				$"Target method '{targetDef.FullName}' is a P/Invoke method and has no managed IL body.",
				callerKey,
				callSiteRef.FullName,
				ilOffset
			);
			return (
				DependencySupportStatus.UnsupportedNoBodyPInvoke,
				"P/Invoke method",
				diagnostic
			);
		}

		if (targetDef.IsInternalCall)
		{
			var diagnostic = new DependencyDiagnostic(
				"AXM1004",
				DiagnosticSeverity.Error,
				$"Target method '{targetDef.FullName}' is a runtime internal call (InternalCall) and has no IL body.",
				callerKey,
				callSiteRef.FullName,
				ilOffset
			);
			return (
				DependencySupportStatus.UnsupportedNoBodyInternalCall,
				"InternalCall method",
				diagnostic
			);
		}

		if (!targetDef.HasBody)
		{
			var diagnostic = new DependencyDiagnostic(
				"AXM1005",
				DiagnosticSeverity.Error,
				$"Target method '{targetDef.FullName}' has no IL body.",
				callerKey,
				callSiteRef.FullName,
				ilOffset
			);
			return (DependencySupportStatus.UnsupportedNoBody, "Method has no body", diagnostic);
		}

		return (DependencySupportStatus.Supported, null, null);
	}

	private static DependencyDiagnostic? CheckCallSiteDispatch(
		MethodReference callSiteRef,
		MethodDefinition? targetDef,
		DependencyCallKind callKind,
		string callerKey,
		int ilOffset
	)
	{
		if (targetDef is null)
		{
			return null;
		}

		if (targetDef.DeclaringType.IsInterface)
		{
			return new DependencyDiagnostic(
				"AXM1007",
				DiagnosticSeverity.Error,
				$"Call to interface method '{callSiteRef.FullName}' requires runtime interface dispatch, which is not yet supported.",
				callerKey,
				callSiteRef.FullName,
				ilOffset
			);
		}

		if (
			callKind == DependencyCallKind.CallVirt
			&& targetDef.IsVirtual
			&& !targetDef.IsFinal
			&& !targetDef.DeclaringType.IsValueType
			&& !targetDef.DeclaringType.IsSealed
		)
		{
			return new DependencyDiagnostic(
				"AXM1007",
				DiagnosticSeverity.Error,
				$"Virtual call to '{callSiteRef.FullName}' requires runtime dynamic dispatch (vtable), which is not yet supported.",
				callerKey,
				callSiteRef.FullName,
				ilOffset
			);
		}

		return null;
	}

	private static bool IsFrameworkAssembly(AssemblyDefinition assembly)
	{
		var name = assembly.Name.Name;
		return string.Equals(name, "System.Private.CoreLib", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(name, "System.Runtime", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase)
			|| name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
			|| name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);
	}
}

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Axiom.Native.Compiler.Tests;

public class MethodDependencyResolverTests
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
	public void Resolve_CallChain_TraversesAllTransitiveDependencies()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		var rootNode = graph.Roots.FirstOrDefault(r => r.Reference.Name == "RootChain");
		Assert.NotNull(rootNode);

		var stepANode = graph.Nodes.FirstOrDefault(n => n.Reference.Name == "StepA");
		var stepBNode = graph.Nodes.FirstOrDefault(n => n.Reference.Name == "StepB");
		var stepCNode = graph.Nodes.FirstOrDefault(n => n.Reference.Name == "StepC");

		Assert.NotNull(stepANode);
		Assert.NotNull(stepBNode);
		Assert.NotNull(stepCNode);

		Assert.True(stepANode.IsSupported);
		Assert.True(stepBNode.IsSupported);
		Assert.True(stepCNode.IsSupported);

		Assert.Contains(rootNode.OutgoingCalls, e => e.Target == stepANode);
		Assert.Contains(stepANode.OutgoingCalls, e => e.Target == stepBNode);
		Assert.Contains(stepBNode.OutgoingCalls, e => e.Target == stepCNode);
	}

	[Fact]
	public void Resolve_SharedDependency_DeduplicatesNodeAndKeepsIncomingEdges()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		var alphaNode = graph.Roots.FirstOrDefault(r => r.Reference.Name == "RootAlpha");
		var betaNode = graph.Roots.FirstOrDefault(r => r.Reference.Name == "RootBeta");

		Assert.NotNull(alphaNode);
		Assert.NotNull(betaNode);

		Assert.Equal(OptimizeFor.ExecutionSpeed, alphaNode.ExplicitOptimizeFor);
		Assert.Equal(OptimizeFor.BuildSize, betaNode.ExplicitOptimizeFor);

		var sharedNodes = graph.Nodes.Where(n => n.Reference.Name == "SharedHelper").ToList();
		Assert.Single(sharedNodes);

		var sharedNode = sharedNodes[0];
		Assert.Null(sharedNode.ExplicitOptimizeFor);
		Assert.False(sharedNode.IsRoot);
		Assert.True(sharedNode.IsSupported);

		Assert.Equal(2, sharedNode.IncomingCalls.Count);
		Assert.Contains(sharedNode.IncomingCalls, e => e.Caller == alphaNode);
		Assert.Contains(sharedNode.IncomingCalls, e => e.Caller == betaNode);
	}

	[Fact]
	public void Resolve_DirectRecursion_HandlesCycleWithoutInfiniteLoop()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		var factNode = graph.Roots.FirstOrDefault(r => r.Reference.Name == "Factorial");
		Assert.NotNull(factNode);

		Assert.Contains(factNode.OutgoingCalls, e => e.Target == factNode);
		Assert.Contains(factNode.IncomingCalls, e => e.Caller == factNode);
	}

	[Fact]
	public void Resolve_MutualRecursion_HandlesCycleBetweenNodes()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		var pingNode = graph.Roots.FirstOrDefault(r => r.Reference.Name == "Ping");
		var pongNode = graph.Nodes.FirstOrDefault(n => n.Reference.Name == "Pong");

		Assert.NotNull(pingNode);
		Assert.NotNull(pongNode);

		Assert.Contains(pingNode.OutgoingCalls, e => e.Target == pongNode);
		Assert.Contains(pongNode.OutgoingCalls, e => e.Target == pingNode);
	}

	[Fact]
	public void Resolve_Overloads_CreatesSeparateNodesForDistinctSignatures()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		var callerNode = graph.Roots.FirstOrDefault(r => r.Reference.Name == "CallOverloads");
		Assert.NotNull(callerNode);

		var calcSingleParam = graph.Nodes.FirstOrDefault(n =>
			n.Reference.Name == "Calculate" && n.Reference.Parameters.Count == 1
		);
		var calcTwoParams = graph.Nodes.FirstOrDefault(n =>
			n.Reference.Name == "Calculate" && n.Reference.Parameters.Count == 2
		);

		Assert.NotNull(calcSingleParam);
		Assert.NotNull(calcTwoParams);
		Assert.NotEqual(calcSingleParam.Key, calcTwoParams.Key);

		Assert.Contains(callerNode.OutgoingCalls, e => e.Target == calcSingleParam);
		Assert.Contains(callerNode.OutgoingCalls, e => e.Target == calcTwoParams);
	}

	[Fact]
	public void Resolve_GenericCalls_PreservesSpecializationsAndReportsDiagnostic()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		var callerNode = graph.Roots.FirstOrDefault(r => r.Reference.Name == "CallerWithGenerics");
		Assert.NotNull(callerNode);

		var intSpecialization = graph.Nodes.FirstOrDefault(n =>
			n.Key.Contains("GenericTarget") && n.Key.Contains("Int32")
		);
		var doubleSpecialization = graph.Nodes.FirstOrDefault(n =>
			n.Key.Contains("GenericTarget") && n.Key.Contains("Double")
		);

		Assert.NotNull(intSpecialization);
		Assert.NotNull(doubleSpecialization);
		Assert.NotEqual(intSpecialization.Key, doubleSpecialization.Key);

		Assert.Equal(intSpecialization.Definition, doubleSpecialization.Definition);

		Assert.Equal(
			DependencySupportStatus.UnsupportedGenericSpecialization,
			intSpecialization.SupportStatus
		);
		Assert.Equal(
			DependencySupportStatus.UnsupportedGenericSpecialization,
			doubleSpecialization.SupportStatus
		);

		Assert.Contains(
			graph.Diagnostics,
			d =>
				d.Code == "AXM1006"
				&& d.TargetReference != null
				&& d.TargetReference.Contains("Int32")
		);
		Assert.Contains(
			graph.Diagnostics,
			d =>
				d.Code == "AXM1006"
				&& d.TargetReference != null
				&& d.TargetReference.Contains("Double")
		);
	}

	[Fact]
	public void Resolve_NonVirtualCallVirt_IsSupported()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		var callerNode = graph.Roots.FirstOrDefault(r =>
			r.Reference.Name == "CallNonVirtualOnTarget"
		);
		Assert.NotNull(callerNode);

		var targetNode = graph.Nodes.FirstOrDefault(n => n.Reference.Name == "NonVirtualMethod");
		Assert.NotNull(targetNode);

		Assert.Equal(DependencySupportStatus.Supported, targetNode.SupportStatus);
		Assert.True(targetNode.IsSupported);

		var edge = callerNode.OutgoingCalls.FirstOrDefault(e => e.Target == targetNode);
		Assert.NotNull(edge);
		Assert.Equal(DependencyCallKind.CallVirt, edge.CallKind);
	}

	[Fact]
	public void Resolve_DynamicDispatch_InterfaceCallReportsDiagnostic()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		var callerNode = graph.Roots.FirstOrDefault(r => r.Reference.Name == "CallInterface");
		Assert.NotNull(callerNode);

		var interfaceMethodNode = graph.Nodes.FirstOrDefault(n => n.Reference.Name == "Compute");
		Assert.NotNull(interfaceMethodNode);

		Assert.Equal(
			DependencySupportStatus.UnsupportedDynamicDispatch,
			interfaceMethodNode.SupportStatus
		);
		Assert.False(interfaceMethodNode.IsSupported);

		Assert.Contains(
			graph.Diagnostics,
			d =>
				d.Code == "AXM1007"
				&& d.CallerMethod != null
				&& d.CallerMethod.Contains("CallInterface")
		);
	}

	[Fact]
	public void Resolve_DynamicDispatch_VirtualMethodReportsDiagnostic()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		var callerNode = graph.Roots.FirstOrDefault(r => r.Reference.Name == "CallVirtual");
		Assert.NotNull(callerNode);

		var virtualMethodNode = graph.Nodes.FirstOrDefault(n =>
			n.Reference.Name == "ComputeVirtual"
		);
		Assert.NotNull(virtualMethodNode);

		Assert.Equal(
			DependencySupportStatus.UnsupportedDynamicDispatch,
			virtualMethodNode.SupportStatus
		);
		Assert.False(virtualMethodNode.IsSupported);

		Assert.Contains(
			graph.Diagnostics,
			d =>
				d.Code == "AXM1007"
				&& d.CallerMethod != null
				&& d.CallerMethod.Contains("CallVirtual")
		);
	}

	[Fact]
	public void Resolve_NoBodyMethod_PInvokeReportsDiagnostic()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		var callerNode = graph.Roots.FirstOrDefault(r => r.Reference.Name == "CallPInvoke");
		Assert.NotNull(callerNode);

		var pInvokeNode = graph.Nodes.FirstOrDefault(n =>
			n.Reference.Name == "GetCurrentProcessId"
		);
		Assert.NotNull(pInvokeNode);

		Assert.Equal(DependencySupportStatus.UnsupportedNoBodyPInvoke, pInvokeNode.SupportStatus);
		Assert.False(pInvokeNode.IsSupported);

		Assert.Contains(
			graph.Diagnostics,
			d =>
				d.Code == "AXM1003"
				&& d.CallerMethod != null
				&& d.CallerMethod.Contains("CallPInvoke")
		);
	}

	[Fact]
	public void Resolve_Diagnostics_AreDeterministicAndSorted()
	{
		var path = GetTestAssemblyPath();
		using var context = NativeAssemblyLoader.Load(path);

		var graph = MethodDependencyResolver.Resolve(context);

		Assert.NotEmpty(graph.Diagnostics);

		for (int i = 0; i < graph.Diagnostics.Count - 1; i++)
		{
			var current = graph.Diagnostics[i];
			var next = graph.Diagnostics[i + 1];

			Assert.True(
				current.CompareTo(next) <= 0,
				$"Diagnostics are not sorted: '{current}' followed by '{next}'"
			);
		}
	}
}

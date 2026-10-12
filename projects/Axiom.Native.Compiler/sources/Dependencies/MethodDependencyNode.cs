using System;
using System.Collections.Generic;
using Mono.Cecil;

namespace Axiom.Native;

public sealed class MethodDependencyNode
{
	private readonly List<MethodDependencyEdge> _outgoingCalls = new();
	private readonly List<MethodDependencyEdge> _incomingCalls = new();

	public string Key { get; }
	public MethodReference Reference { get; }
	public MethodDefinition? Definition { get; }
	public bool IsRoot { get; }
	public OptimizeFor? ExplicitOptimizeFor { get; }
	public DependencySupportStatus SupportStatus { get; internal set; }
	public string? UnsupportedReason { get; internal set; }
	public bool IsExternal { get; }

	public IReadOnlyList<MethodDependencyEdge> OutgoingCalls => _outgoingCalls;
	public IReadOnlyList<MethodDependencyEdge> IncomingCalls => _incomingCalls;

	public bool IsSupported => SupportStatus == DependencySupportStatus.Supported;

	public MethodDependencyNode(
		string key,
		MethodReference reference,
		MethodDefinition? definition,
		bool isRoot,
		OptimizeFor? explicitOptimizeFor,
		DependencySupportStatus supportStatus,
		string? unsupportedReason = null,
		bool isExternal = false
	)
	{
		Key = key ?? throw new ArgumentNullException(nameof(key));
		Reference = reference ?? throw new ArgumentNullException(nameof(reference));
		Definition = definition;
		IsRoot = isRoot;
		ExplicitOptimizeFor = explicitOptimizeFor;
		SupportStatus = supportStatus;
		UnsupportedReason = unsupportedReason;
		IsExternal = isExternal;
	}

	internal void AddOutgoingCall(MethodDependencyEdge edge)
	{
		_outgoingCalls.Add(edge);
	}

	internal void AddIncomingCall(MethodDependencyEdge edge)
	{
		_incomingCalls.Add(edge);
	}

	public override string ToString()
	{
		var rootMarker = IsRoot ? " [Root]" : string.Empty;
		var status =
			SupportStatus == DependencySupportStatus.Supported
				? "Supported"
				: $"Unsupported: {SupportStatus}";
		return $"{Key}{rootMarker} ({status})";
	}
}

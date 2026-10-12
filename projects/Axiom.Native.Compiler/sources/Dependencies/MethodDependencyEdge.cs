using System;
using Mono.Cecil;

namespace Axiom.Native;

public sealed class MethodDependencyEdge
{
	public MethodDependencyNode Caller { get; }
	public MethodDependencyNode Target { get; }
	public DependencyCallKind CallKind { get; }
	public int IlOffset { get; }
	public MethodReference CallSiteReference { get; }

	public MethodDependencyEdge(
		MethodDependencyNode caller,
		MethodDependencyNode target,
		DependencyCallKind callKind,
		int ilOffset,
		MethodReference callSiteReference
	)
	{
		Caller = caller ?? throw new ArgumentNullException(nameof(caller));
		Target = target ?? throw new ArgumentNullException(nameof(target));
		CallKind = callKind;
		IlOffset = ilOffset;
		CallSiteReference =
			callSiteReference ?? throw new ArgumentNullException(nameof(callSiteReference));
	}

	public override string ToString()
	{
		return $"{Caller.Key} -[{CallKind} @ 0x{IlOffset:X4}]-> {Target.Key}";
	}
}

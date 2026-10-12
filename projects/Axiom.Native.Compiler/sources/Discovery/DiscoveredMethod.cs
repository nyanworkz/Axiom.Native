using Mono.Cecil;

namespace Axiom.Native;

public sealed class DiscoveredMethod
{
	public MethodDefinition Method { get; }
	public OptimizeFor OptimizeFor { get; }
	public bool IsInheritedFromStruct { get; }

	public DiscoveredMethod(
		MethodDefinition method,
		OptimizeFor optimizeFor,
		bool isInheritedFromStruct
	)
	{
		Method = method;
		OptimizeFor = optimizeFor;
		IsInheritedFromStruct = isInheritedFromStruct;
	}

	public override string ToString()
	{
		var source = IsInheritedFromStruct ? "inherited from struct" : "explicit";
		return $"{Method.FullName} ({OptimizeFor}, {source})";
	}
}

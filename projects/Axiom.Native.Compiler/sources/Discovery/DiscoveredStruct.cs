using Mono.Cecil;

namespace Axiom.Native;

public sealed class DiscoveredStruct
{
	public TypeDefinition Type { get; }
	public OptimizeFor OptimizeFor { get; }

	public DiscoveredStruct(TypeDefinition type, OptimizeFor optimizeFor)
	{
		Type = type;
		OptimizeFor = optimizeFor;
	}

	public override string ToString()
	{
		return $"{Type.FullName} ({OptimizeFor})";
	}
}

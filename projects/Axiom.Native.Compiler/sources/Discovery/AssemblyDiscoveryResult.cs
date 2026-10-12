using System.Collections.Generic;
using System.Linq;

namespace Axiom.Native;

public sealed class AssemblyDiscoveryResult
{
	public IReadOnlyList<DiscoveredMethod> Methods { get; }
	public IReadOnlyList<DiscoveredStruct> Structs { get; }
	public IReadOnlyList<DiscoveryDiagnostic> Diagnostics { get; }

	public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

	public AssemblyDiscoveryResult(
		IReadOnlyList<DiscoveredMethod> methods,
		IReadOnlyList<DiscoveredStruct> structs,
		IReadOnlyList<DiscoveryDiagnostic> diagnostics
	)
	{
		Methods = methods;
		Structs = structs;
		Diagnostics = diagnostics;
	}
}

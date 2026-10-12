using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Axiom.Native;

public sealed class MethodDependencyGraph
{
	private readonly Dictionary<string, MethodDependencyNode> _nodesByKey;

	public IReadOnlyList<MethodDependencyNode> Roots { get; }
	public IReadOnlyCollection<MethodDependencyNode> Nodes => _nodesByKey.Values;
	public IReadOnlyList<DependencyDiagnostic> Diagnostics { get; }

	public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

	public MethodDependencyGraph(
		IReadOnlyList<MethodDependencyNode> roots,
		Dictionary<string, MethodDependencyNode> nodesByKey,
		IReadOnlyList<DependencyDiagnostic> diagnostics
	)
	{
		Roots = roots ?? throw new ArgumentNullException(nameof(roots));
		_nodesByKey = nodesByKey ?? throw new ArgumentNullException(nameof(nodesByKey));
		Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
	}

	public MethodDependencyNode? FindNode(string key)
	{
		ArgumentNullException.ThrowIfNull(key);
		return _nodesByKey.GetValueOrDefault(key);
	}

	public MethodDependencyNode? FindNode(MethodReference reference)
	{
		ArgumentNullException.ThrowIfNull(reference);
		return _nodesByKey.GetValueOrDefault(reference.FullName);
	}
}

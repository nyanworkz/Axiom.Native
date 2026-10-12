using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Axiom.Native;

public sealed class LlvmIrGenerationResult
{
	public MethodDefinition? Method { get; }
	public string? LlvmIr { get; }
	public IReadOnlyList<LlvmIrDiagnostic> Diagnostics { get; }

	public bool IsSuccess => LlvmIr is not null && !HasErrors;
	public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

	public LlvmIrGenerationResult(
		MethodDefinition? method,
		string? llvmIr,
		IReadOnlyList<LlvmIrDiagnostic> diagnostics
	)
	{
		Method = method;
		LlvmIr = llvmIr;
		Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
	}

	public override string ToString()
	{
		var name = Method?.FullName ?? "Unknown";
		return IsSuccess
			? $"{name} -> Success ({LlvmIr?.Length ?? 0} chars IR)"
			: $"{name} -> Failed ({Diagnostics.Count} diagnostics)";
	}
}


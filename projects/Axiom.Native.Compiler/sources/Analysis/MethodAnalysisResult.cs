using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Axiom.Native;

public sealed class MethodAnalysisResult
{
	public MethodDefinition? Method { get; }
	public MethodReference? Reference { get; }
	public IlAnalysisSupportStatus SupportStatus { get; }
	public IlControlFlowGraph? Cfg { get; }
	public IReadOnlyList<IlAnalysisDiagnostic> Diagnostics { get; }

	public bool IsSupported => SupportStatus == IlAnalysisSupportStatus.Supported;
	public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

	public MethodAnalysisResult(
		MethodDefinition? method,
		IlAnalysisSupportStatus supportStatus,
		IlControlFlowGraph? cfg,
		IReadOnlyList<IlAnalysisDiagnostic> diagnostics,
		MethodReference? reference = null
	)
	{
		Method = method;
		Reference = reference ?? method;
		SupportStatus = supportStatus;
		Cfg = cfg;
		Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
	}

	public override string ToString()
	{
		var name = Reference?.FullName ?? Method?.FullName ?? "Unknown";
		return $"{name} -> {SupportStatus} ({Diagnostics.Count} diagnostics)";
	}
}

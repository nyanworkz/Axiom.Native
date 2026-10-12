using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Axiom.Native;

public sealed class NativeEmissionResult
{
	public MethodDefinition? Method { get; }
	public string? ObjectFilePath { get; }
	public IReadOnlyList<NativeEmissionDiagnostic> Diagnostics { get; }
	public string? StandardOutput { get; }
	public string? StandardError { get; }
	public int? ExitCode { get; }

	public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
	public bool IsSuccess =>
		!HasErrors
		&& ObjectFilePath is not null
		&& File.Exists(ObjectFilePath)
		&& new FileInfo(ObjectFilePath).Length > 0;

	public NativeEmissionResult(
		MethodDefinition? method,
		string? objectFilePath,
		IReadOnlyList<NativeEmissionDiagnostic> diagnostics,
		string? standardOutput = null,
		string? standardError = null,
		int? exitCode = null
	)
	{
		Method = method;
		ObjectFilePath = objectFilePath;
		Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
		StandardOutput = standardOutput;
		StandardError = standardError;
		ExitCode = exitCode;
	}

	public override string ToString()
	{
		var name = Method?.FullName ?? "Unknown";
		return IsSuccess
			? $"{name} -> Object file created at '{ObjectFilePath}' ({new FileInfo(ObjectFilePath!).Length} bytes)"
			: $"{name} -> Failed ({Diagnostics.Count} diagnostics, exit {ExitCode})";
	}
}

using System;

namespace Axiom.Native;

public sealed class NativeEmissionDiagnostic
	: IEquatable<NativeEmissionDiagnostic>,
		IComparable<NativeEmissionDiagnostic>
{
	public string Code { get; }
	public DiagnosticSeverity Severity { get; }
	public string Message { get; }
	public string MethodFullName { get; }
	public string? ToolName { get; }
	public int? ExitCode { get; }

	public NativeEmissionDiagnostic(
		string code,
		DiagnosticSeverity severity,
		string message,
		string methodFullName,
		string? toolName = null,
		int? exitCode = null
	)
	{
		Code = code ?? throw new ArgumentNullException(nameof(code));
		Severity = severity;
		Message = message ?? throw new ArgumentNullException(nameof(message));
		MethodFullName = methodFullName ?? throw new ArgumentNullException(nameof(methodFullName));
		ToolName = toolName;
		ExitCode = exitCode;
	}

	public bool Equals(NativeEmissionDiagnostic? other)
	{
		if (other is null)
		{
			return false;
		}

		return Code == other.Code
			&& Severity == other.Severity
			&& MethodFullName == other.MethodFullName
			&& ToolName == other.ToolName
			&& ExitCode == other.ExitCode
			&& Message == other.Message;
	}

	public override bool Equals(object? obj) => Equals(obj as NativeEmissionDiagnostic);

	public override int GetHashCode()
	{
		return HashCode.Combine(
			Code,
			(int)Severity,
			MethodFullName,
			ToolName,
			HashCode.Combine(ExitCode, Message)
		);
	}

	public int CompareTo(NativeEmissionDiagnostic? other)
	{
		if (other is null)
		{
			return 1;
		}

		var methodCompare = string.CompareOrdinal(MethodFullName, other.MethodFullName);
		if (methodCompare != 0)
		{
			return methodCompare;
		}

		var codeCompare = string.CompareOrdinal(Code, other.Code);
		if (codeCompare != 0)
		{
			return codeCompare;
		}

		return string.CompareOrdinal(Message, other.Message);
	}

	public override string ToString()
	{
		var toolPart =
			ToolName != null
				? $" ({ToolName}{(ExitCode.HasValue ? $" exit {ExitCode}" : "")})"
				: "";
		return $"[{Code}][{Severity}] {MethodFullName}{toolPart}: {Message}";
	}
}

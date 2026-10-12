using System;

namespace Axiom.Native;

public sealed class IlAnalysisDiagnostic
	: IEquatable<IlAnalysisDiagnostic>,
		IComparable<IlAnalysisDiagnostic>
{
	public string Code { get; }
	public DiagnosticSeverity Severity { get; }
	public string Message { get; }
	public string MethodFullName { get; }
	public int? IlOffset { get; }

	public IlAnalysisDiagnostic(
		string code,
		DiagnosticSeverity severity,
		string message,
		string methodFullName,
		int? ilOffset = null
	)
	{
		Code = code ?? throw new ArgumentNullException(nameof(code));
		Severity = severity;
		Message = message ?? throw new ArgumentNullException(nameof(message));
		MethodFullName = methodFullName ?? throw new ArgumentNullException(nameof(methodFullName));
		IlOffset = ilOffset;
	}

	public bool Equals(IlAnalysisDiagnostic? other)
	{
		if (other is null)
		{
			return false;
		}

		return Code == other.Code
			&& Severity == other.Severity
			&& MethodFullName == other.MethodFullName
			&& IlOffset == other.IlOffset
			&& Message == other.Message;
	}

	public override bool Equals(object? obj) => Equals(obj as IlAnalysisDiagnostic);

	public override int GetHashCode()
	{
		return HashCode.Combine(Code, (int)Severity, MethodFullName, IlOffset, Message);
	}

	public int CompareTo(IlAnalysisDiagnostic? other)
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

		var offsetCompare = (IlOffset ?? -1).CompareTo(other.IlOffset ?? -1);
		if (offsetCompare != 0)
		{
			return offsetCompare;
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
		var loc = IlOffset.HasValue ? $"{MethodFullName}+0x{IlOffset:X4}" : MethodFullName;
		return $"[{Code}][{Severity}] {loc}: {Message}";
	}
}

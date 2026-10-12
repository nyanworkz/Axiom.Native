using System;

namespace Axiom.Native;

public sealed class DependencyDiagnostic
	: IEquatable<DependencyDiagnostic>,
		IComparable<DependencyDiagnostic>
{
	public string Code { get; }
	public DiagnosticSeverity Severity { get; }
	public string Message { get; }
	public string? CallerMethod { get; }
	public string? TargetReference { get; }
	public int? IlOffset { get; }

	public DependencyDiagnostic(
		string code,
		DiagnosticSeverity severity,
		string message,
		string? callerMethod = null,
		string? targetReference = null,
		int? ilOffset = null
	)
	{
		Code = code ?? throw new ArgumentNullException(nameof(code));
		Severity = severity;
		Message = message ?? throw new ArgumentNullException(nameof(message));
		CallerMethod = callerMethod;
		TargetReference = targetReference;
		IlOffset = ilOffset;
	}

	public bool Equals(DependencyDiagnostic? other)
	{
		if (other is null)
		{
			return false;
		}

		return Code == other.Code
			&& Severity == other.Severity
			&& CallerMethod == other.CallerMethod
			&& TargetReference == other.TargetReference
			&& IlOffset == other.IlOffset;
	}

	public override bool Equals(object? obj) => Equals(obj as DependencyDiagnostic);

	public override int GetHashCode()
	{
		return HashCode.Combine(Code, (int)Severity, CallerMethod, TargetReference, IlOffset);
	}

	public int CompareTo(DependencyDiagnostic? other)
	{
		if (other is null)
		{
			return 1;
		}

		var callerCompare = string.CompareOrdinal(
			CallerMethod ?? string.Empty,
			other.CallerMethod ?? string.Empty
		);
		if (callerCompare != 0)
		{
			return callerCompare;
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

		return string.CompareOrdinal(
			TargetReference ?? string.Empty,
			other.TargetReference ?? string.Empty
		);
	}

	public override string ToString()
	{
		var location = CallerMethod is not null
			? (IlOffset.HasValue ? $"{CallerMethod}+0x{IlOffset:X4}" : CallerMethod)
			: "global";

		return $"[{Code}][{Severity}] {location}: {Message}";
	}
}

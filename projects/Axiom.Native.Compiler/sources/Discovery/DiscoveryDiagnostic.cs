namespace Axiom.Native;

public enum DiagnosticSeverity
{
	Info,
	Warning,
	Error,
}

public sealed class DiscoveryDiagnostic
{
	public DiagnosticSeverity Severity { get; }
	public string Message { get; }
	public string? MemberFullName { get; }

	public DiscoveryDiagnostic(
		DiagnosticSeverity severity,
		string message,
		string? memberFullName = null
	)
	{
		Severity = severity;
		Message = message;
		MemberFullName = memberFullName;
	}

	public override string ToString()
	{
		return MemberFullName is null
			? $"[{Severity}] {Message}"
			: $"[{Severity}] {MemberFullName}: {Message}";
	}
}

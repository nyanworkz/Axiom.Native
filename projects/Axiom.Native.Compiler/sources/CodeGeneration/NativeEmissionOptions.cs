namespace Axiom.Native;

public enum LlvmBackendTool
{
	Auto,
	Llc,
	Clang,
}

public sealed class NativeEmissionOptions
{
	public string? OutputPath { get; set; }
	public OptimizeFor Optimization { get; set; } = OptimizeFor.ExecutionSpeed;
	public string? TargetTriple { get; set; }
	public string? ToolchainPath { get; set; }
	public bool VerifyIr { get; set; } = true;
	public int TimeoutMs { get; set; } = 30000;
	public LlvmBackendTool PreferredTool { get; set; } = LlvmBackendTool.Auto;

	public static NativeEmissionOptions Default => new();
}

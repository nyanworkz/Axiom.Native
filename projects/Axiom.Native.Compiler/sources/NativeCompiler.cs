using System;

namespace Axiom.Native;

public static class NativeCompiler
{
	public static LlvmIrGenerationResult GenerateLlvmIr(MethodAnalysisResult analysisResult)
	{
		ArgumentNullException.ThrowIfNull(analysisResult);
		return LlvmIrGenerator.Generate(analysisResult);
	}

	public static NativeEmissionResult EmitNativeObject(
		LlvmIrGenerationResult irResult,
		NativeEmissionOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(irResult);
		return NativeCodeEmitter.EmitObjectFile(irResult, options);
	}

	public static NativeEmissionResult EmitNativeObject(
		MethodAnalysisResult analysisResult,
		NativeEmissionOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(analysisResult);
		var irResult = LlvmIrGenerator.Generate(analysisResult);
		if (!irResult.IsSuccess)
		{
			var diag = new NativeEmissionDiagnostic(
				"AXM4001",
				DiagnosticSeverity.Error,
				$"Cannot emit native code because LLVM IR generation failed ({irResult.Diagnostics.Count} diagnostics).",
				analysisResult.Method?.FullName ?? "Unknown"
			);
			return new NativeEmissionResult(analysisResult.Method, null, new[] { diag });
		}

		return NativeCodeEmitter.EmitObjectFile(irResult, options);
	}
}

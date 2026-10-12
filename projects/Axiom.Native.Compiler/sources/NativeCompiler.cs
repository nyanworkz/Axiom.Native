using System;

namespace Axiom.Native;

public static class NativeCompiler
{
	public static LlvmIrGenerationResult GenerateLlvmIr(MethodAnalysisResult analysisResult)
	{
		ArgumentNullException.ThrowIfNull(analysisResult);
		return LlvmIrGenerator.Generate(analysisResult);
	}
}

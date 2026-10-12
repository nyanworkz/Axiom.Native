namespace Axiom.Native;

public enum IlAnalysisSupportStatus
{
	Supported,
	UnsupportedInstruction,
	UnsupportedType,
	UnsupportedExceptionHandling,
	InvalidIlStack,
	InvalidControlFlow,
	InvalidMetadata,
	UnsupportedMethodCall,
}

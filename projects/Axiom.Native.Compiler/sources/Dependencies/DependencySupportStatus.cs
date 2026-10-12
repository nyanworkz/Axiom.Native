namespace Axiom.Native;

public enum DependencySupportStatus
{
	Supported,
	UnsupportedNoBodyAbstract,
	UnsupportedNoBodyPInvoke,
	UnsupportedNoBodyInternalCall,
	UnsupportedNoBody,
	UnsupportedGenericSpecialization,
	UnsupportedDynamicDispatch,
	UnsupportedUnresolvedReference,
}

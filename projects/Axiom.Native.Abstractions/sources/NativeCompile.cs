using System;

namespace Axiom.Native;

[AttributeUsage(
	AttributeTargets.Method | AttributeTargets.Struct,
	AllowMultiple = false,
	Inherited = false
)]
public class NativeCompileAttribute : Attribute
{
	public OptimizeFor OptimizeFor { get; }

	public NativeCompileAttribute(OptimizeFor optimizeFor = OptimizeFor.ExecutionSpeed)
	{
		if (!Enum.IsDefined(optimizeFor))
		{
			throw new ArgumentOutOfRangeException(
				nameof(optimizeFor),
				optimizeFor,
				"Unknown native optimization mode."
			);
		}

		OptimizeFor = optimizeFor;
	}
}

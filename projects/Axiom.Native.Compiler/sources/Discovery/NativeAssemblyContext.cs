using System;
using Mono.Cecil;

namespace Axiom.Native;

public sealed class NativeAssemblyContext : IDisposable
{
	private bool _disposed;

	public AssemblyDefinition Assembly { get; }
	public IAssemblyResolver Resolver { get; }

	public NativeAssemblyContext(AssemblyDefinition assembly, IAssemblyResolver resolver)
	{
		Assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
		Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		Assembly.Dispose();

		if (Resolver is IDisposable disposableResolver)
		{
			disposableResolver.Dispose();
		}
	}
}

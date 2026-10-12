# Axiom.Native Development Rules

## Project goal

Axiom.Native is a .NET 10 library and compiler that translates a restricted subset of C# methods into native machine code using LLVM.

## Architecture

- `Axiom.Native.Abstractions` contains public attributes and shared API contracts.
- `Axiom.Native.Compiler` reads managed assemblies, discovers compilation roots, analyzes IL, builds an internal IR, and generates native code through LLVM.
- `Axiom.Native.Runtime` validates, loads, and invokes native artifacts.
- Mono.Cecil is used for assembly metadata and IL inspection.
- Runtime must not depend on LLVM or the Compiler implementation.

## Development rules

- Inspect the existing repository before proposing changes.
- Preserve existing public API unless a change is explicitly requested.
- Do not invent library APIs or assume package versions are compatible. Verify them against installed package metadata or official documentation.
- Make small, focused changes.
- Do not implement unrelated features.
- Do not silently skip unsupported methods or fall back to managed execution.
- Keep compiler diagnostics deterministic and actionable.
- Add tests for every behavior change.
- Run `dotnet build` and relevant `dotnet test` commands after changes.
- Report the exact commands executed and whether they passed.
- Never claim tests passed unless they were actually run.
- Do not overwrite unrelated user changes.
- Ask before adding dependencies, changing project structure, or introducing major abstractions.

## Current milestone

Use Mono.Cecil to load a managed assembly and discover methods marked with `[NativeCompile]`.

The current scope is metadata discovery only. Do not implement LLVM code generation, native loading, or assembly rewriting unless explicitly requested.

## Definition of done

- The solution builds.
- Tests cover the implemented behavior.
- Existing tests still pass.
- The implementation matches the requested scope.
- Any limitations are documented.

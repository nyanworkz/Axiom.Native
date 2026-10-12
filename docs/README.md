# Axiom.Native Documentation

Welcome to the technical documentation for **Axiom.Native**.

Axiom.Native is a .NET 10 library and ahead-of-time (AOT) compilation toolchain designed to translate a restricted, high-performance subset of managed C# methods into native machine code using LLVM.

---

## 📌 Implementation Status

The project is developed in milestones. The table below outlines the current status of each compilation and runtime pipeline stage:

| Pipeline Stage | Component | Status | Description |
|---|---|:---:|---|
| **Root Discovery** | [`NativeAssemblyLoader`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Discovery/NativeAssemblyLoader.cs) | **Implemented** | Loads managed assemblies via Mono.Cecil and discovers methods and structs attributed with `[NativeCompile]`. |
| **Dependency Resolution** | [`MethodDependencyResolver`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Dependencies/MethodDependencyResolver.cs) | **Implemented** | Traces transitive calls, evaluates direct/virtual/generic calls, and produces a dependency graph with terminal boundary nodes. |
| **IL Analysis & Validation** | [`MethodIlAnalyzer`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Analysis/MethodIlAnalyzer.cs) | **Implemented** | Builds CFG, evaluates basic blocks via a worklist algorithm, checks typed stack balance, and verifies primitive arithmetic operations. |
| **LLVM IR Generation** | [`LlvmIrGenerator`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/CodeGeneration/LlvmIrGenerator.cs) | **Implemented** | Translates validated CFGs and evaluation stack operations into verifiable textual LLVM Intermediate Representation. |
| **Native Code Emission** | [`NativeCodeEmitter`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/CodeGeneration/NativeCodeEmitter.cs) | **Implemented** | Compiles verified LLVM IR into host platform native object files (`.obj` / `.o`) via discovered `clang` or `llc`. |
| **Dynamic Library Linking** | Toolchain linker integration | *Planned* | Links native object files into dynamic shared libraries (`.dll`, `.so`, `.dylib`) for runtime loading. |
| **Assembly Rewriting** | Compiler pipeline | *Planned* | Replaces managed method bodies with P/Invoke or native invocation trampolines. |
| **Native Runtime** | `NativeRuntime` | *Planned* | Validates, loads, and dispatches native artifacts dynamically at runtime. |
| **Tooling & CLI** | CLI / MSBuild Task | *Planned* | Provides command-line interfaces and MSBuild build-time integration. |

---

## 📚 Documentation Index

1. [**System Overview**](overview.md)
   High-level architecture, problem statement, compilation goals, pipeline workflow diagram, and current limitations.
2. [**Architecture & Design**](architecture.md)
   Solution organization, assembly responsibilities, memory management, `AssemblyDefinition` lifetime, and component interactions.
3. [**Public API & Attributes**](public-api.md)
   Specification for [`NativeCompileAttribute`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Abstractions/sources/NativeCompileAttribute.cs) and [`OptimizeFor`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Abstractions/sources/OptimizeFor.cs).
4. [**Assembly Discovery Subsystem**](compiler/discovery.md)
   Inspection via Mono.Cecil, in-memory loading, struct discovery, nested type handling, and diagnostic reporting.
5. [**Dependency Resolution Subsystem**](compiler/dependencies.md)
   Call graph construction, `call` vs `callvirt` inspection, recursion/cycle handling, generic calls, and BCL terminal boundaries.
6. [**IL Analysis & CFG Subsystem**](compiler/il-analysis.md)
   Control-flow graph construction, typed evaluation stack simulation, supported IL opcodes, worklist convergence, and branch verification.
7. [**LLVM IR Generation Subsystem**](compiler/llvm-ir-generation.md)
   Translation to LLVM IR, type mapping, entry block local allocation, evaluation stack SSA lowering, phi node merge reconciliation, and LLVM verification.
8. [**Native Code Emission Subsystem**](compiler/native-code-emission.md)
   Toolchain discovery (`clang`, `llc`, `opt`), host target triple detection, optimization flag mapping, temporary file hygiene, and native object compilation.
9. [**Diagnostics Reference**](diagnostics.md)
   Comprehensive catalog of compiler diagnostic codes (`AXM1001`–`AXM1007`, `AXM2001`–`AXM2011`, `AXM3001`–`AXM3008`, and `AXM4001`–`AXM4008`), failure modes, and resolutions.
10. [**Current Limitations**](limitations.md)
    Detailed list of unsupported C# language features, CLR types, instructions, emission constraints, and CLR vs LLVM integer semantics.
11. [**Testing & Verification**](testing.md)
    Test suite structure, 85-test coverage matrix across Milestones 1–5, and verification via `opt -passes=verify` and real object file emission.
12. [**Roadmap & Next Steps**](roadmap.md)
    Milestone status, completed milestones, next architectural steps (Milestone 6: Dynamic Library Linking & Trampoline Generation), and long-term milestones.

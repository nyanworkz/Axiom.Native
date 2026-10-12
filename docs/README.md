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
| **LLVM IR Generation** | `NativeCompiler` | *Planned* | Translates validated IL CFG and instructions into LLVM Intermediate Representation. |
| **Native Compilation** | Toolchain integration | *Planned* | Compiles LLVM IR into native shared objects (`.dll`, `.so`, `.dylib`) for target architectures. |
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
7. [**Diagnostics Reference**](diagnostics.md)
   Comprehensive catalog of compiler diagnostic codes (`AXM1001`–`AXM1007` and `AXM2001`–`AXM2011`), failure modes, and resolutions.
8. [**Current Limitations**](limitations.md)
   Detailed list of unsupported C# language features, CLR types, and instructions in the current milestone.
9. [**Testing & Verification**](testing.md)
   Test suite structure, coverage matrix, fixture setup via `Axiom.Native.Compiler.Tests.TestAssembly`, and verification commands.
10. [**Roadmap & Next Steps**](roadmap.md)
    Milestone status, next architectural steps (LLVM IR generation, code emitter), and long-term milestones.


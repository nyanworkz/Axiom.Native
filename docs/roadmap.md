# Axiom.Native Roadmap & Milestones

This document outlines the current progress and scheduled milestones for **Axiom.Native**.

---

## 1. Milestone Status Matrix

| Milestone | Subsystem | Status | Deliverables |
|---|---|:---:|---|
| **Milestone 1** | **Assembly & Root Discovery** | **Implemented** | - [`NativeAssemblyLoader`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Discovery/NativeAssemblyLoader.cs) with immediate in-memory loading.<br/>- Attribute extraction for methods and structs.<br/>- [`OptimizeFor`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Abstractions/sources/OptimizeFor.cs) strategy inheritance and overrides.<br/>- 9 unit tests. |
| **Milestone 2** | **Dependency Resolution** | **Implemented** | - [`MethodDependencyResolver`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Dependencies/MethodDependencyResolver.cs).<br/>- Transitive call graph ([`MethodDependencyGraph`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Dependencies/MethodDependencyGraph.cs)).<br/>- Detection of dynamic dispatch (`callvirt`), generics, P/Invoke, and BCL terminal boundaries.<br/>- Diagnostic codes `AXM1001`–`AXM1007`.<br/>- 11 unit tests. |
| **Milestone 3** | **IL Analysis & Verification** | **Implemented** | - [`MethodIlAnalyzer`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Analysis/MethodIlAnalyzer.cs).<br/>- Control Flow Graph ([`IlControlFlowGraph`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Analysis/IlControlFlowGraph.cs)).<br/>- Abstract typed stack simulation (`I4`, `I8`).<br/>- Fixpoint worklist algorithm for loop convergence.<br/>- Diagnostic codes `AXM2001`–`AXM2011`.<br/>- 30 unit tests. |
| **Milestone 4** | **LLVM IR Generation** | **Implemented** | - [`LlvmIrGenerator`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/CodeGeneration/LlvmIrGenerator.cs) generating valid textual LLVM IR.<br/>- Mapping CLR primitives to LLVM integer types (`i1`, `i8`, `i16`, `i32`, `i64`).<br/>- Entry block `alloca` layout respecting `MethodBody.InitLocals`.<br/>- SSA evaluation stack translation and `phi` node construction at block merges.<br/>- Masked bitwise shift counts preventing poison/UB.<br/>- Signed/unsigned instruction selection.<br/>- Diagnostic codes `AXM3001`–`AXM3008`.<br/>- 25 unit tests verified against LLVM 18.1.8 `opt -passes=verify`. |
| **Milestone 5** | **Native Code Emission** | **Implemented** | - [`NativeCodeEmitter`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/CodeGeneration/NativeCodeEmitter.cs) compiling verified LLVM IR to native object files (`.obj` / `.o`).<br/>- Dynamic [`LlvmToolchain`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/CodeGeneration/LlvmToolchain.cs) discovery (`clang`, `llc`, `opt`) without hardcoded paths.<br/>- Dynamic host target triple detection (`x86_64-pc-windows-msvc`, etc.).<br/>- Safe asynchronous process execution avoiding pipe deadlocks.<br/>- Clean temporary file management and output existence checks.<br/>- Diagnostic codes `AXM4001`–`AXM4008`.<br/>- 10 unit and integration tests. |
| **Milestone 6** | **Shared Library Linking** | *Planned* | - Invoking linkers (`lld-link`, `ld.lld`, or system linker) to link native object files into shared dynamic libraries (`.dll`, `.so`, `.dylib`).<br/>- Exporting C-ABI symbol tables. |
| **Milestone 7** | **Managed Assembly Rewriting** | *Planned* | - Replacing original managed method bodies with P/Invoke calls to compiled native binaries.<br/>- Stub generation and assembly metadata rewriting via Mono.Cecil. |
| **Milestone 8** | **Native Runtime Host** | *Planned* | - Implementation of [`Axiom.Native.Runtime`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Runtime/sources/NativeRuntime.cs).<br/>- Dynamic loading, verification, and function pointer dispatch of native artifacts. |
| **Milestone 9** | **Build Tooling & CLI** | *Planned* | - Standalone command-line compiler (`axiom-native`).<br/>- MSBuild post-compile target for seamless project integration. |

---

## 2. Immediate Next Step: Milestone 6 (Shared Library Linking)

Milestone 6 will link produced machine object files (`.obj` / `.o`) into shared dynamic libraries (`.dll` on Windows, `.so` on Linux, `.dylib` on macOS).

### Key Objectives for Milestone 6:
1. **Linker Toolchain Discovery**:
   - Locate platform linkers (`lld-link`, `clang`, `link.exe`, `ld.lld`).
2. **Export Symbol Configuration**:
   - Configure exported symbols and module definition files (`.def` on Windows) to expose compiled kernel function pointers with stable C-ABI symbols.
3. **Shared Object Generation**:
   - Link object files into shared libraries suitable for dynamic loading via `NativeLibrary` / `dlopen`.

---

## 3. Long-Term Evolution

- **Floating-Point Arithmetic**: Introduce `F4` (`float`) and `F8` (`double`) stack simulation and LLVM floating-point instruction generation (`fadd`, `fsub`, `fmul`, `fdiv`).
- **Value Type Struct Passing**: Support passing fixed-layout structs by value and pointer.
- **SIMD Intrinsics**: Direct lowering of SIMD vector types (`Vector128<T>`, `Vector256<T>`) to LLVM native vector types and intrinsic instructions.

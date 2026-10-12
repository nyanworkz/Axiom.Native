# Axiom.Native Roadmap & Milestones

This document outlines the current progress and scheduled milestones for **Axiom.Native**.

---

## 1. Milestone Status Matrix

| Milestone | Subsystem | Status | Deliverables |
|---|---|:---:|---|
| **Milestone 1** | **Assembly & Root Discovery** | **Implemented** | - [`NativeAssemblyLoader`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Discovery/NativeAssemblyLoader.cs) with immediate in-memory loading.<br/>- Attribute extraction for methods and structs.<br/>- [`OptimizeFor`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Abstractions/sources/OptimizeFor.cs) strategy inheritance and overrides.<br/>- 9 unit tests. |
| **Milestone 2** | **Dependency Resolution** | **Implemented** | - [`MethodDependencyResolver`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Dependencies/MethodDependencyResolver.cs).<br/>- Transitive call graph ([`MethodDependencyGraph`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Dependencies/MethodDependencyGraph.cs)).<br/>- Detection of dynamic dispatch (`callvirt`), generics, P/Invoke, and BCL terminal boundaries.<br/>- Diagnostic codes `AXM1001`–`AXM1007`.<br/>- 11 unit tests. |
| **Milestone 3** | **IL Analysis & Verification** | **Implemented** | - [`MethodIlAnalyzer`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Analysis/MethodIlAnalyzer.cs).<br/>- Control Flow Graph ([`IlControlFlowGraph`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Analysis/IlControlFlowGraph.cs)).<br/>- Abstract typed stack simulation (`I4`, `I8`).<br/>- Fixpoint worklist algorithm for loop convergence.<br/>- Diagnostic codes `AXM2001`–`AXM2011`.<br/>- 30 unit tests. |
| **Milestone 4** | **LLVM IR Generation** | **Implemented** | - [`LlvmIrGenerator`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/CodeGeneration/LlvmIrGenerator.cs) generating valid textual LLVM IR.<br/>- Mapping CLR primitives to LLVM integer types (`i1`, `i8`, `i16`, `i32`, `i64`).<br/>- Entry block `alloca` layout for local variables.<br/>- SSA evaluation stack translation and `phi` node construction at block merges.<br/>- Masked bitwise shift counts preventing poison/UB.<br/>- Signed/unsigned instruction selection.<br/>- Diagnostic codes `AXM3001`–`AXM3008`.<br/>- 23 unit tests verified against LLVM 18.1.8 `opt -passes=verify`. |
| **Milestone 5** | **Native Code Emission** | *Planned* | - Invoking LLVM backend / toolchain to compile generated LLVM IR into native object code.<br/>- Target machine configuration (x86_64, AArch64).<br/>- Emitting native shared libraries (`.dll`, `.so`, `.dylib`).<br/>- Exporting C-ABI native entry point symbols. |
| **Milestone 6** | **Managed Assembly Rewriting** | *Planned* | - Replacing original managed method bodies with P/Invoke calls to compiled native binaries.<br/>- Stub generation and assembly metadata rewriting via Mono.Cecil. |
| **Milestone 7** | **Runtime Loader & Invoker** | *Planned* | - Implementation of [`Axiom.Native.Runtime`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Runtime/sources/NativeRuntime.cs).<br/>- Dynamic loading and function pointer invocation of native artifacts. |
| **Milestone 8** | **Build Tooling & CLI** | *Planned* | - Standalone command-line compiler (`axiom-native`).<br/>- MSBuild post-compile target for seamless project integration. |

---

## 2. Immediate Next Step: Milestone 5 (Native Code Emission)

Milestone 5 will compile the validated LLVM IR text into machine object code and dynamic shared libraries.

### Key Objectives for Milestone 5:
1. **Target Architecture & Triple Configuration**:
   - Query target architecture from host environment or compiler options (e.g. `x86_64-pc-windows-msvc`, `x86_64-unknown-linux-gnu`, `aarch64-apple-darwin`).
2. **Toolchain Invocation & Linking**:
   - Integrate with `clang` or `llc` + system linker (`lld-link`, `ld.lld`) to assemble native `.obj` files into shared objects (`.dll`, `.so`, `.dylib`).
3. **Symbol Exporting**:
   - Decorate target function symbols with appropriate export attributes (`dllexport` / `default` visibility) so compiled methods can be called via standard C ABI.
4. **Optimization Pipeline Application**:
   - Drive optimization passes based on [`OptimizeFor`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Abstractions/sources/OptimizeFor.cs):
     - `Debug`: `-O0`, preserve symbols.
     - `ExecutionSpeed`: `-O3`, vectorization, unrolling.
     - `BuildSize`: `-Oz`, size optimization.

---

## 3. Long-Term Evolution

- **Floating-Point Arithmetic**: Introduce `F4` (`float`) and `F8` (`double`) stack simulation and LLVM floating-point instruction generation (`fadd`, `fsub`, `fmul`, `fdiv`).
- **Value Type Struct Passing**: Support passing fixed-layout structs by value and pointer.
- **SIMD Intrinsics**: Direct lowering of SIMD vector types (`Vector128<T>`, `Vector256<T>`) to LLVM native vector types and intrinsic instructions.

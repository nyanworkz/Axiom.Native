# Axiom.Native Roadmap & Milestones

This document outlines the current progress and scheduled milestones for **Axiom.Native**.

---

## 1. Milestone Status Matrix

| Milestone | Subsystem | Status | Deliverables |
|---|---|:---:|---|
| **Milestone 1** | **Assembly & Root Discovery** | **Implemented** | - [`NativeAssemblyLoader`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Discovery/NativeAssemblyLoader.cs) with immediate in-memory loading.<br/>- Attribute extraction for methods and structs.<br/>- [`OptimizeFor`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Abstractions/sources/OptimizeFor.cs) strategy inheritance and overrides.<br/>- 9 unit tests. |
| **Milestone 2** | **Dependency Resolution** | **Implemented** | - [`MethodDependencyResolver`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Dependencies/MethodDependencyResolver.cs).<br/>- Transitive call graph ([`MethodDependencyGraph`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Dependencies/MethodDependencyGraph.cs)).<br/>- Detection of dynamic dispatch (`callvirt`), generics, P/Invoke, and BCL terminal boundaries.<br/>- Diagnostic codes `AXM1001`–`AXM1007`.<br/>- 11 unit tests. |
| **Milestone 3** | **IL Analysis & Verification** | **Implemented** | - [`MethodIlAnalyzer`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Analysis/MethodIlAnalyzer.cs).<br/>- Control Flow Graph ([`IlControlFlowGraph`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Analysis/IlControlFlowGraph.cs)).<br/>- Abstract typed stack simulation (`I4`, `I8`).<br/>- Fixpoint worklist algorithm for loop convergence.<br/>- Diagnostic codes `AXM2001`–`AXM2011`.<br/>- 30 unit tests. |
| **Milestone 4** | **LLVM IR Generation** | *Planned* | - Integration with LLVM C-API / LLVMSharp.<br/>- Mapping `IlBasicBlock` to LLVM Basic Blocks.<br/>- Lowering evaluation stack operations into LLVM IR instructions.<br/>- Applying optimization pipelines based on `OptimizeFor`. |
| **Milestone 5** | **Native Code Emission** | *Planned* | - Target machine configuration (x86_64, AArch64).<br/>- Emitting native shared libraries (`.dll`, `.so`, `.dylib`).<br/>- Exporting C-ABI native entry point symbols. |
| **Milestone 6** | **Managed Assembly Rewriting** | *Planned* | - Replacing original managed method bodies with P/Invoke calls to compiled native binaries.<br/>- Stub generation and assembly metadata rewriting. |
| **Milestone 7** | **Runtime Loader & Invoker** | *Planned* | - Implementation of [`Axiom.Native.Runtime`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Runtime/sources/NativeRuntime.cs).<br/>- Dynamic loading and function pointer invocation of native artifacts. |
| **Milestone 8** | **Build Tooling & CLI** | *Planned* | - Standalone command-line compiler (`axiom-native`).<br/>- MSBuild post-compile target for seamless project integration. |

---

## 2. Immediate Next Step: Milestone 4 (LLVM IR Generation)

Milestone 4 will bridge the validated IL representation to native machine code using LLVM.

### Key Objectives for Milestone 4:
1. **Binding Selection & Verification**:
   - Evaluate compatible LLVM package bindings for .NET 10 (e.g., `LLVMSharp` or direct `DllImport` bindings to `libLLVM`).
   - Confirm cross-platform support (Windows, Linux, macOS).
2. **Type Mapping**:
   - Map primitive CLR types to LLVM types:
     - `I4` $\to$ `i32` (with sign-extension or zero-extension for sub-word types like `i8`/`i16`).
     - `I8` $\to$ `i64`.
     - `void` $\to$ `void`.
3. **Stack to SSA Lowering**:
   - Strategy A: Map evaluation stack pushes/pops to temporary LLVM `alloca` memory locations, relying on LLVM's `mem2reg` optimization pass to convert them to SSA registers.
   - Strategy B: Direct symbolic SSA register tracking during stack simulation.
4. **CFG Lowering**:
   - Translate each [`IlBasicBlock`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Analysis/IlBasicBlock.cs) into an `LLVMBasicBlockRef`.
   - Lower branching opcodes (`br`, `beq`, `bge`, etc.) into `LLVMBuildBr` and `LLVMBuildCondBr`.
5. **Optimization Strategy Application**:
   - Configure LLVM `PassBuilder` based on the method's [`OptimizeFor`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Abstractions/sources/OptimizeFor.cs) setting:
     - `Debug`: `-O0`, preserve debug locators.
     - `ExecutionSpeed`: `-O3`, vectorization, aggressive inlining, loop unrolling.
     - `BuildSize`: `-Oz`, size-focused optimization.

---

## 3. Long-Term Evolution

- **Floating-Point Arithmetic**: Introduce `F4` (`float`) and `F8` (`double`) stack simulation and LLVM floating-point instruction generation.
- **Value Type Struct Passing**: Support passing fixed-layout structs by value and pointer.
- **SIMD Intrinsics**: Direct lowering of SIMD vector types (`Vector128<T>`, `Vector256<T>`) to LLVM native vector types and intrinsic instructions.


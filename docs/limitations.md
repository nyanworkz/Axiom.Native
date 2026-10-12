# Axiom.Native Current Limitations

This document provides a comprehensive accounting of current constraints, unsupported language features, and semantic differences in the current milestone of **Axiom.Native**.

---

## 1. Categorization of Limitations

Limitations are categorized into four distinct groups:
1. **Intentionally Unsupported**: Features that violate the zero-overhead native design goals of Axiom.Native.
2. **Current Milestone Restrictions**: Features temporarily disallowed in IL Analysis and code generation that may be addressed in future milestones.
3. **Semantic Differences (CLR vs. LLVM)**: Divergence between managed CLR runtime behavior and native LLVM machine execution.
4. **Pending Downstream Pipeline Stages**: Downstream native compilation and runtime components that are designed but not yet implemented.

---

## 2. Intentionally Unsupported (Architectural Non-Goals)

| Feature | Rationale |
|---|---|
| **Silent Managed Fallback** | Axiom.Native never falls back to JIT execution silently. If a kernel cannot be compiled natively, compilation fails with clear diagnostic errors. |
| **Garbage-Collected Object Allocations** | Allocating managed reference objects (`newobj`, `newarr`, `box`) inside native kernels introduces GC barriers and safepoints, defeating the performance objective of native execution. |
| **Reflection & Runtime Dynamic Dispatch** | Resolving methods or invoking types dynamically at runtime requires the full CLR execution engine. Native kernels must be statically determinable. |
| **Exception Handling (`try/catch/finally`)** | Managed exception propagation across unmanaged/managed stack frames incurs significant unwinding overhead and complex ABI considerations. Native kernels should communicate errors via return codes. |

---

## 3. Current Milestone Restrictions (IL Subset)

Methods subjected to IL Analysis and LLVM IR Generation must conform to a strict primitive subset:

### Supported Types
- **Integers**: `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `char`.
- **Booleans**: `bool`.
- **Return**: Any of the above, or `void`.

### Unsupported Types & Features
- **Floating-Point Arithmetic**: `float` (`System.Single`) and `double` (`System.Double`) are rejected with `AXM2002`. Support for IEEE 754 floating-point types (`F4` / `F8` stack types) is planned for a future milestone.
- **Reference Types**: `string`, classes, interfaces, delegates, and arrays trigger `AXM2002`.
- **Value Types & User Structs**: Although structs with `[NativeCompile]` can be discovered as containers for methods, passing user structs as arguments or return values is not yet supported in IL analysis (`AXM2002`).
- **By-Reference Parameters (`ref`, `out`, `in`, pointers)**: Managed pointers (`&`) and unmanaged pointers (`*`) are rejected with `AXM2002`.
- **Instance Methods Accessing `this`**: Instance methods can be discovered, but accessing `this` via `ldarg.0` is rejected with `AXM2002` (and `AXM3004`) until struct layout and native pointer semantics are implemented.
- **Argument Mutation (`starg`)**: Mutating method arguments directly via `starg` or `starg.s` is disallowed (`AXM2001`). Use local variables instead.
- **In-Method Invocations (`call`, `callvirt`)**: Although Milestone 2 analyzes and builds dependency call graphs, Milestone 3 (IL Analysis) rejects direct or virtual calls (`AXM2011`) until call lowered signatures and ABI interop are completed.

---

## 4. Semantic Differences Between CLR and LLVM

When generating LLVM IR from CIL bytecode, semantic differences between the CLR specification (ECMA-335) and LLVM Language Reference require deliberate architectural handling:

### 1. Division by Zero
- **CLR Specification**: Integer division by zero (`div`, `div.un`, `rem`, `rem.un`) throws `System.DivideByZeroException`.
- **LLVM Semantics**: Division by zero in `sdiv`, `udiv`, `srem`, and `urem` is **undefined behavior (UB)** at the hardware level. In native code, integer division by zero triggers a hardware trap (e.g., `SIGFPE` / integer division exception `0xC0000094` on x86/Windows).
- **Axiom.Native Approach**: Axiom.Native does not synthesize software branch checks before every division in performance kernels. Callers must ensure divisors are non-zero.

### 2. Signed Integer Overflow
- **CLR Specification**: In default C# (`unchecked` context), signed integer overflow silently wraps in two's complement. In `checked` context, it throws `System.OverflowException`.
- **LLVM Semantics**: In LLVM, `add`, `sub`, and `mul` can take `nsw` (No Signed Wrap) or `nuw` (No Unsigned Wrap) attributes. If overflow occurs with `nsw`, it produces a poison value or undefined behavior.
- **Axiom.Native Approach**: Axiom.Native **never emits `nsw` or `nuw`** on arithmetic operations. All arithmetic operations wrap in two's complement, matching C#'s default `unchecked` semantics.

### 3. Signed Minimum Integer Division (`int.MinValue / -1`)
- **CLR Specification**: Dividing `-2147483648` by `-1` in `unchecked` context wraps or produces `-2147483648` on two's complement machines.
- **LLVM Semantics**: `sdiv` of minimum signed integer by `-1` without `nsw` produces two's-complement overflow.

### 4. Shift Amounts
- **CLR Specification**: In CIL, the shift count operand for 32-bit integers is masked by `0x1F` (31), and for 64-bit integers by `0x3F` (63).
- **LLVM Semantics**: If the shift amount in `shl`, `ashr`, or `lshr` is greater than or equal to the integer bit width, the result is poison.
- **Axiom.Native Handling**: To guarantee 100% semantic fidelity with the CLR, `LlvmIrGenerator` **explicitly masks shift counts** with `and i32 %shift, 31` (for `i32`) and `and i64 %shift, 63` (for `i64`) before invoking shift instructions.

### 5. Conversions & Narrowing
- Narrowing conversions (`conv.i4` from `i64`) discard high-order bits via LLVM `trunc`.
- Widening conversions preserve signedness: `conv.i8` emits `sext` (sign-extension), whereas `conv.u8` emits `zext` (zero-extension).

---

## 5. Pipeline Implementation Status

```
[Discovery]          --> IMPLEMENTED (Milestone 1)
[Dependencies]       --> IMPLEMENTED (Milestone 2)
[IL Analysis]        --> IMPLEMENTED (Milestone 3)
[LLVM IR Generation] --> IMPLEMENTED (Milestone 4)
[Native Compilation] --> PLANNED (Milestone 5: Shared library generation via LLVM)
[Assembly Rewriting] --> PLANNED (Milestone 6: Replacing managed bodies with native dispatch)
[Native Runtime]     --> PLANNED (Milestone 7: NativeRuntime host & dynamic loader)
[MSBuild / CLI]      --> PLANNED (Milestone 8: Build integration)
```

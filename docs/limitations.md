# Axiom.Native Current Limitations

This document provides a comprehensive accounting of current constraints, unsupported language features, and design boundaries in the current milestone of **Axiom.Native**.

---

## 1. Categorization of Limitations

Limitations are categorized into three distinct groups:
1. **Intentionally Unsupported**: Features that violate the zero-overhead native design goals of Axiom.Native.
2. **Current Milestone Restrictions**: Features temporarily disallowed in IL Analysis that may be addressed in future milestones.
3. **Pending Pipeline Stages**: Downstream compiler and runtime components that are designed but not yet implemented.

---

## 2. Intentionally Unsupported (Architectural Non-Goals)

| Feature | Rationale |
|---|---|
| **Silent Managed Fallback** | Axiom.Native never falls back to JIT execution silently. If a kernel cannot be compiled natively, compilation fails with clear diagnostic errors. |
| **Garbage-Collected Object Allocations** | Allocating managed reference objects (`newobj`, `newarr`, `box`) inside native kernels introduces GC barriers and safepoints, defeating the performance objective of native execution. |
| **Reflection & Runtime Dynamic Dispatch** | Resolving methods or invoking types dynamically at runtime requires the full CLR execution engine. Native kernels must be statically determinable. |
| **Exception Handling (`try/catch/finally`)** | Managed exception propagation across unmanaged/managed stack frames incurs significant unwinding overhead and complex ABI considerations. Native kernels should communicate errors via return codes. |

---

## 3. Current Milestone Restrictions (IL Analysis Subset)

In the current milestone (Milestones 1–3), methods subjected to [`MethodIlAnalyzer`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler/sources/Analysis/MethodIlAnalyzer.cs) must conform to a strict primitive subset:

### Supported Types
- **Integers**: `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `char`.
- **Booleans**: `bool`.
- **Return**: Any of the above, or `void`.

### Unsupported Types & Features
- **Floating-Point Arithmetic**: `float` (`System.Single`) and `double` (`System.Double`) are currently rejected with `AXM2002`. Support for IEEE 754 floating-point types (`F4` / `F8` stack types) will be introduced in an upcoming milestone.
- **Reference Types**: `string`, classes, interfaces, delegates, and arrays trigger `AXM2002`.
- **Value Types & User Structs**: Although structs with `[NativeCompile]` can be discovered as containers for methods, passing user structs as arguments or return values is not yet supported in IL analysis (`AXM2002`).
- **By-Reference Parameters (`ref`, `out`, `in`, pointers)**: Managed pointers (`&`) and unmanaged pointers (`*`) are rejected with `AXM2002`.
- **Instance Methods Accessing `this`**: Instance methods can be discovered, but accessing `this` via `ldarg.0` is rejected with `AXM2002` until struct layout and native pointer semantics are implemented.
- **Argument Mutation (`starg`)**: Mutating method arguments directly via `starg` or `starg.s` is disallowed (`AXM2001`). Use local variables instead.
- **In-Method Invocations (`call`, `callvirt`)**: Although Milestone 2 analyzes and builds dependency call graphs, Milestone 3 (IL Analysis) rejects direct or virtual calls (`AXM2011`) until call lowered signatures and ABI interop are completed.

---

## 4. Pending Pipeline Stages

The following components are planned but **not yet implemented**:

```
[Discovery]      --> IMPLEMENTED
[Dependencies]   --> IMPLEMENTED
[IL Analysis]    --> IMPLEMENTED
[LLVM Lowering]  --> PLANNED (NativeCompiler is currently an empty stub)
[Native Output]  --> PLANNED (Shared library generation)
[IL Rewriting]   --> PLANNED (Replacing managed bodies with native dispatch)
[Native Runtime] --> PLANNED (NativeRuntime is currently an empty stub)
[MSBuild / CLI]  --> PLANNED (Tooling integration)
```

---

## 5. Kernel Writing Guidelines

To ensure that your C# methods pass discovery and analysis today, structure them according to the following template:

```csharp
public static class FastMath
{
    // ✅ SUPPORTED: Static method, primitive integer types, loops, branches, bitwise ops
    [NativeCompile(OptimizeFor.ExecutionSpeed)]
    public static int ComputeGcd(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }

    // ❌ UNSUPPORTED: Floating-point math (AXM2002)
    [NativeCompile]
    public static double AddDoubles(double a, double b) => a + b;

    // ❌ UNSUPPORTED: Exception handling blocks (AXM2003)
    [NativeCompile]
    public static int SafeDivide(int a, int b)
    {
        try { return a / b; }
        catch { return 0; }
    }

    // ❌ UNSUPPORTED: Reference types (AXM2002)
    [NativeCompile]
    public static int GetStringLength(string s) => s.Length;
}
```


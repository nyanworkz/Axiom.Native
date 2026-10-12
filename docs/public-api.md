# Axiom.Native Public API & Attributes

The public API surface of Axiom.Native is contained entirely within the [`Axiom.Native.Abstractions`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Abstractions) assembly.

---

## 1. `NativeCompileAttribute`

[`NativeCompileAttribute`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Abstractions/sources/NativeCompileAttribute.cs) marks methods and value types (structs) as candidates for native ahead-of-time compilation via Axiom.Native.

### Declaration

```csharp
namespace Axiom.Native;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class NativeCompileAttribute : Attribute
{
    public NativeCompileAttribute()
    {
    }

    public NativeCompileAttribute(OptimizeFor optimizeFor)
    {
        OptimizeFor = optimizeFor;
    }

    public OptimizeFor OptimizeFor { get; set; } = OptimizeFor.ExecutionSpeed;
}
```

### Usage Attributes & Constraints
- **Target Applicability**: Valid only on `AttributeTargets.Method` and `AttributeTargets.Struct`. Attributing classes or interfaces is not supported.
- **Inheritance**: `Inherited = false`. Subclasses or interface implementations do not automatically inherit native compilation.
- **Multiplicity**: `AllowMultiple = false`. Duplicate attributes on the same member are disallowed.

---

## 2. `OptimizeFor` Enum

The [`OptimizeFor`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Abstractions/sources/OptimizeFor.cs) enum specifies the compilation strategy requested for the target code unit.

```csharp
namespace Axiom.Native;

public enum OptimizeFor
{
    Debug = 0,
    ExecutionSpeed = 1,
    BuildSize = 2
}
```

| Member | Value | Description |
|---|:---:|---|
| `Debug` | `0` | Requests debuggable native code generation, prioritizing symbol generation and suppressing aggressive inlining or loop unrolling. |
| `ExecutionSpeed` | `1` | Default optimization strategy. Maximizes runtime throughput, enabling aggressive LLVM optimizations, auto-vectorization, and unrolling. |
| `BuildSize` | `2` | Prioritizes minimal native machine code size, suppressing aggressive loop unrolling and code duplication. |

---

## 3. Semantic Behaviors & Precedence

### Method-Level Attribution
When applied directly to a method, the method is registered as a standalone compilation root:

```csharp
public static class MathKernels
{
    [NativeCompile] // Defaults to OptimizeFor.ExecutionSpeed
    public static int Add(int a, int b) => a + b;

    [NativeCompile(OptimizeFor.BuildSize)]
    public static long ComputeFactorial(int n)
    {
        // ...
    }
}
```

### Struct-Level Attribution
When applied to a `struct`, every instance or static method declared within that struct inherits native compilation automatically:

```csharp
[NativeCompile(OptimizeFor.ExecutionSpeed)]
public struct Vector2D
{
    public int X;
    public int Y;

    // Inherits OptimizeFor.ExecutionSpeed from enclosing struct
    public int DotProduct(int otherX, int otherY) => X * otherX + Y * otherY;

    // Overrides struct-level strategy with explicit BuildSize
    [NativeCompile(OptimizeFor.BuildSize)]
    public int MagnitudeSquared() => X * X + Y * Y;
}
```

### Override Priority
If both a struct and an internal method declare `[NativeCompile]`:
1. The **method-level attribute takes precedence** for the `OptimizeFor` setting.
2. The method is registered once, retaining the method's explicit strategy.

---

## 4. What Attribution Does NOT Guarantee

Applying `[NativeCompile]` does **not** guarantee successful native compilation:

1. **No Silent Fallback**: The attribute is a strict compilation directive. If a method uses unsupported IL instructions (e.g., floating-point, reference types, exception handlers) or calls unsupported external APIs, the compiler **rejects the method with deterministic diagnostic errors**.
2. **Analysis Prerequisite**: All attributed methods and their transitively reachable dependencies must pass [IL Analysis](compiler/il-analysis.md).
3. **No Automatic P/Invoke Trampoline Generation Yet**: In the current milestone, discovery and analysis are performed, but managed body replacement has not yet been implemented (see [Roadmap](roadmap.md)).


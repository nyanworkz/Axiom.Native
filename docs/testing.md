# Axiom.Native Testing & Verification

Axiom.Native maintains high test rigor to guarantee deterministic compiler behavior and avoid silent regressions.

---

## 1. Test Projects Organization

The test infrastructure consists of two dedicated projects:

```
projects/
├── Axiom.Native.Compiler.Tests.TestAssembly/ # Target assembly compiled with real Roslyn C# code
│   └── sources/
│       └── SampleTargets.cs                  # Real methods and structs covering valid and invalid patterns
└── Axiom.Native.Compiler.Tests/             # xUnit test suite
    └── sources/
        ├── NativeAssemblyLoaderTests.cs      # Milestone 1: 9 tests
        ├── MethodDependencyResolverTests.cs  # Milestone 2: 11 tests
        └── MethodIlAnalyzerTests.cs          # Milestone 3: 30 tests
```

### Why a Dedicated `TestAssembly`?
Testing metadata and IL analysis on synthetic Cecil objects alone can introduce divergence from actual Roslyn compiler outputs. `Axiom.Native.Compiler.Tests.TestAssembly` is compiled as a standard managed .NET 10 assembly containing realistic language constructs:
- Loops (`for`, `while`, `do-while`)
- Conditionals (`if`, `switch`, ternary operator)
- Nested structs and classes
- Recursive algorithms
- Generic methods and classes
- Unsupported constructs (exception handlers, floating-point math, reference objects)

---

## 2. Test Coverage Matrix

As of Milestone 3, the test suite contains **50 tests** with 100% pass rate:

| Test Class | Milestone | Tests Count | Scope & Focus Areas |
|---|:---:|:---:|---|
| [`NativeAssemblyLoaderTests`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler.Tests/sources/NativeAssemblyLoaderTests.cs) | Discovery | 9 | - Loading from file path and memory stream.<br/>- Immediate reading mode without file locking.<br/>- Root discovery of static and instance methods.<br/>- Struct-level attribution and method inheritance.<br/>- Method-level `OptimizeFor` override over struct defaults.<br/>- Deep recursion across nested types.<br/>- Proper exclusion of unattributed methods. |
| [`MethodDependencyResolverTests`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler.Tests/sources/MethodDependencyResolverTests.cs) | Dependencies | 11 | - Direct invocation tracking (`call`).<br/>- Virtual and interface invocation detection (`callvirt` $\to$ `AXM1007`).<br/>- Recursive call handling and cycle termination.<br/>- Deduplication of visited nodes.<br/>- Identification of foreign BCL assembly boundaries.<br/>- Detection of generic methods and generic declaring types (`AXM1006`).<br/>- Non-existent or abstract method detection. |
| [`MethodIlAnalyzerTests`](file:///d:/Projects/Axiom/Native/projects/Axiom.Native.Compiler.Tests/sources/MethodIlAnalyzerTests.cs) | IL Analysis | 30 | - **Positive verification**: Basic integer arithmetic, loops, bitwise logic, conversions, unary negation, signed/unsigned divisions and comparisons, void return.<br/>- **Malformed IL**: Stack underflow (`AXM2004`), operand type mismatch (`AXM2005`), merge point mismatch (`AXM2006`), invalid branch target (`AXM2007`), invalid return stack (`AXM2008`), out-of-bounds arg/local index (`AXM2009`), missing body (`AXM2010`).<br/>- **Unsupported IL**: Floating-point (`AXM2002`), reference types (`AXM2002`), `this` access (`AXM2002`), exception handling (`AXM2003`), `starg` mutation (`AXM2001`), in-method calls (`AXM2011`). |

---

## 3. Running Tests

### Standard Test Execution
Run all tests across the solution:

```powershell
dotnet test
```

### Verbose Execution with Project Filtering
To run only compiler unit tests with standard console output:

```powershell
dotnet test projects/Axiom.Native.Compiler.Tests/Axiom.Native.Compiler.Tests.csproj --logger "console;verbosity=normal"
```

### Verification Result

```
Passed!  - Failed: 0, Passed: 50, Skipped: 0, Total: 50, Duration: 500 ms - Axiom.Native.Compiler.Tests.dll (net10.0)
```

---

## 4. Continuous Verification Rules

According to the project development rules in `AGENTS.md`:
- Every change to metadata discovery, dependency resolution, or IL analysis must include corresponding positive and negative test cases.
- Tests must be executed using `dotnet test` before marking tasks as complete.
- Test counts and execution results must always be verified and reported accurately.


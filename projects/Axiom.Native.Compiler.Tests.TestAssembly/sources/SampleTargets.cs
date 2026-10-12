using Axiom.Native;

namespace Axiom.Native.Compiler.Tests.TestAssembly;

public static class SimpleMath
{
	[NativeCompile]
	public static int Add(int a, int b) => a + b;

	[NativeCompile(OptimizeFor.Debug)]
	public static int Subtract(int a, int b) => a - b;

	[NativeCompile(OptimizeFor.BuildSize)]
	public static int Multiply(int a, int b) => a * b;

	public static int UnmarkedMethod(int a, int b) => a / b;
}

public class OuterClass
{
	public class InnerClass
	{
		[NativeCompile]
		public static int DeepNestedMethod() => 42;
	}
}

[NativeCompile(OptimizeFor.BuildSize)]
public struct NativePoint
{
	public int X;
	public int Y;

	public int GetNormSquared() => X * X + Y * Y;

	[NativeCompile(OptimizeFor.ExecutionSpeed)]
	public void FastReset()
	{
		X = 0;
		Y = 0;
	}
}

public class StructContainer
{
	[NativeCompile]
	public struct NestedStruct
	{
		public int Value;

		public int GetValue() => Value;
	}
}

public static class CallChainSample
{
	[NativeCompile]
	public static int RootChain(int x) => StepA(x) + 1;

	public static int StepA(int x) => StepB(x) * 2;

	public static int StepB(int x) => StepC(x) - 3;

	public static int StepC(int x) => x + 10;
}

public static class SharedDependencySample
{
	[NativeCompile(OptimizeFor.ExecutionSpeed)]
	public static int RootAlpha(int x) => SharedHelper(x) + 10;

	[NativeCompile(OptimizeFor.BuildSize)]
	public static int RootBeta(int x) => SharedHelper(x) + 20;

	public static int SharedHelper(int x) => x * x;
}

public static class RecursionSample
{
	[NativeCompile]
	public static int Factorial(int n)
	{
		if (n <= 1)
		{
			return 1;
		}

		return n * Factorial(n - 1);
	}
}

public static class MutualRecursionSample
{
	[NativeCompile]
	public static int Ping(int n)
	{
		if (n <= 0)
		{
			return 0;
		}

		return Pong(n - 1);
	}

	public static int Pong(int n)
	{
		if (n <= 0)
		{
			return 1;
		}

		return Ping(n - 1);
	}
}

public static class OverloadSample
{
	[NativeCompile]
	public static int CallOverloads(int a, int b)
	{
		return Calculate(a) + Calculate(a, b);
	}

	public static int Calculate(int a) => a * 2;

	public static int Calculate(int a, int b) => a + b;
}

public static class GenericCallSample
{
	[NativeCompile]
	public static void CallerWithGenerics()
	{
		GenericTarget<int>(1);
		GenericTarget<double>(2.5);
	}

	public static T GenericTarget<T>(T val) => val;
}

public class InstanceCallSample
{
	public int Value { get; set; }

	[NativeCompile]
	public static int CallNonVirtualOnTarget(InstanceCallSample sample)
	{
		return sample.NonVirtualMethod(5);
	}

	public int NonVirtualMethod(int x) => x + 42;
}

public class BasePolymorphic
{
	public virtual int ComputeVirtual(int x) => x * 2;
}

public interface ICalculator
{
	int Compute(int x);
}

public class DynamicDispatchSample
{
	[NativeCompile]
	public static int CallInterface(ICalculator calc, int x)
	{
		return calc.Compute(x);
	}

	[NativeCompile]
	public static int CallVirtual(BasePolymorphic obj, int x)
	{
		return obj.ComputeVirtual(x);
	}
}

public static class NoBodySample
{
	[System.Runtime.InteropServices.DllImport("kernel32.dll")]
	public static extern uint GetCurrentProcessId();

	[NativeCompile]
	public static uint CallPInvoke()
	{
		return GetCurrentProcessId();
	}
}

public static class IlAnalysisPositiveSamples
{
	[NativeCompile]
	public static int Add(int a, int b) => a + b;

	[NativeCompile]
	public static int Subtract(int a, int b) => a - b;

	[NativeCompile]
	public static int Multiply(int a, int b) => a * b;

	[NativeCompile]
	public static int LocalsMethod(int a, int b)
	{
		int x = a + 10;
		int y = b * 2;
		return x + y;
	}

	[NativeCompile]
	public static int ReturnConstant() => 42;

	[NativeCompile]
	public static int ComplexArithmetic(int a, int b, int c)
	{
		return (a + b) * c - (a / 2) + (b % 3);
	}

	[NativeCompile]
	public static int SimpleIfElse(int a, int b)
	{
		if (a > b)
		{
			return a - b;
		}
		else
		{
			return b - a;
		}
	}

	[NativeCompile]
	public static int SumLoop(int n)
	{
		int sum = 0;
		for (int i = 0; i < n; i++)
		{
			sum += i;
		}
		return sum;
	}

	[NativeCompile]
	public static int MultiBranchMerge(int a, int b)
	{
		int result;
		if (a > 10)
		{
			result = 100;
		}
		else if (b > 5)
		{
			result = 50;
		}
		else
		{
			result = 0;
		}
		return result;
	}

	[NativeCompile]
	public static void VoidMethod() { }
}

public static class IlAnalysisNegativeSamples
{
	[NativeCompile]
	public static int TryCatchMethod(int a)
	{
		try
		{
			return a + 1;
		}
		catch
		{
			return 0;
		}
	}

	[NativeCompile]
	public static T GenericMethod<T>(T val) => val;

	[NativeCompile]
	public static double UnsupportedSignatureMethod(double x) => x + 1.0;
}

public class InstanceArgsSample
{
	[NativeCompile]
	public long AddInstanceArgs(int a, long b)
	{
		return a + b;
	}
}

public static class StaticArgsSample
{
	[NativeCompile]
	public static long AddStaticArgs(int a, long b)
	{
		return a + b;
	}
}

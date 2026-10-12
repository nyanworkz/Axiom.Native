using System;
using Mono.Cecil;

namespace Axiom.Native;

public readonly record struct LlvmTypeInfo(
	string LlvmName,
	int BitWidth,
	bool IsSigned,
	bool IsVoid
)
{
	public int Alignment => BitWidth switch
	{
		1 => 1,
		8 => 1,
		16 => 2,
		32 => 4,
		64 => 8,
		_ => 4
	};
}

public static class LlvmTypeHelper
{
	public static bool TryGetTypeInfo(TypeReference type, out LlvmTypeInfo info)
	{
		ArgumentNullException.ThrowIfNull(type);

		switch (type.MetadataType)
		{
			case MetadataType.Boolean:
				info = new LlvmTypeInfo("i1", 1, false, false);
				return true;
			case MetadataType.Byte:
				info = new LlvmTypeInfo("i8", 8, false, false);
				return true;
			case MetadataType.SByte:
				info = new LlvmTypeInfo("i8", 8, true, false);
				return true;
			case MetadataType.Int16:
				info = new LlvmTypeInfo("i16", 16, true, false);
				return true;
			case MetadataType.UInt16:
			case MetadataType.Char:
				info = new LlvmTypeInfo("i16", 16, false, false);
				return true;
			case MetadataType.Int32:
				info = new LlvmTypeInfo("i32", 32, true, false);
				return true;
			case MetadataType.UInt32:
				info = new LlvmTypeInfo("i32", 32, false, false);
				return true;
			case MetadataType.Int64:
				info = new LlvmTypeInfo("i64", 64, true, false);
				return true;
			case MetadataType.UInt64:
				info = new LlvmTypeInfo("i64", 64, false, false);
				return true;
			case MetadataType.Void:
				info = new LlvmTypeInfo("void", 0, false, true);
				return true;
			default:
				info = default;
				return false;
		}
	}

	public static string ToLlvmStackType(IlStackType stackType) => stackType switch
	{
		IlStackType.I4 => "i32",
		IlStackType.I8 => "i64",
		_ => throw new ArgumentOutOfRangeException(nameof(stackType), stackType, "Unknown stack type.")
	};
}


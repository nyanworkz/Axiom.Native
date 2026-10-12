using System;
using System.Collections.Generic;
using Mono.Cecil.Cil;

namespace Axiom.Native;

public sealed class IlBasicBlock
{
	public int Index { get; }
	public int StartOffset { get; }
	public int EndOffset { get; }
	public IReadOnlyList<Instruction> Instructions { get; }
	public List<IlBasicBlock> Predecessors { get; } = new();
	public List<IlBasicBlock> Successors { get; } = new();

	public List<IlStackType>? EntryStack { get; set; }
	public List<IlStackType>? ExitStack { get; set; }
	public bool IsReachable { get; set; }

	public Instruction FirstInstruction => Instructions[0];
	public Instruction LastInstruction => Instructions[^1];

	public IlBasicBlock(int index, IReadOnlyList<Instruction> instructions)
	{
		ArgumentNullException.ThrowIfNull(instructions);
		if (instructions.Count == 0)
		{
			throw new ArgumentException(
				"Basic block must contain at least one instruction.",
				nameof(instructions)
			);
		}

		Index = index;
		Instructions = instructions;
		StartOffset = instructions[0].Offset;
		var last = instructions[^1];
		EndOffset = last.Offset + last.GetSize();
	}

	public override string ToString()
	{
		var reach = IsReachable ? "reachable" : "unreachable";
		return $"Block {Index} [0x{StartOffset:X4}..0x{EndOffset:X4}] ({reach}, {Instructions.Count} instrs)";
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Axiom.Native;

public sealed class IlControlFlowGraph
{
	private readonly Dictionary<int, IlBasicBlock> _blocksByStartOffset = new();
	private readonly List<IlBasicBlock> _blocks = new();

	public MethodDefinition Method { get; }
	public IReadOnlyList<IlBasicBlock> Blocks => _blocks;
	public IlBasicBlock? EntryBlock => _blocks.Count > 0 ? _blocks[0] : null;

	private IlControlFlowGraph(MethodDefinition method)
	{
		Method = method ?? throw new ArgumentNullException(nameof(method));
	}

	public IlBasicBlock? FindBlockByOffset(int offset)
	{
		return _blocksByStartOffset.GetValueOrDefault(offset);
	}

	public static (
		IlControlFlowGraph? Cfg,
		List<IlAnalysisDiagnostic> Diagnostics
	) BuildAndValidate(MethodDefinition method)
	{
		ArgumentNullException.ThrowIfNull(method);

		var diagnostics = new List<IlAnalysisDiagnostic>();
		var cfg = new IlControlFlowGraph(method);

		if (!method.HasBody || method.Body.Instructions.Count == 0)
		{
			diagnostics.Add(
				new IlAnalysisDiagnostic(
					"AXM2010",
					DiagnosticSeverity.Error,
					"Method has no IL instructions.",
					method.FullName
				)
			);
			return (null, diagnostics);
		}

		var instructions = method.Body.Instructions;

		// Ensure instruction offsets are properly set (for in-memory dynamic instructions)
		var currentOffset = 0;
		foreach (var instr in instructions)
		{
			instr.Offset = currentOffset;
			currentOffset += instr.GetSize();
		}

		var instructionSet = new HashSet<Instruction>(instructions);
		var leaderOffsets = new SortedSet<int>();

		// 1. Entry is always a leader
		leaderOffsets.Add(instructions[0].Offset);

		// 2. Identify all leaders and validate branch targets
		for (var i = 0; i < instructions.Count; i++)
		{
			var instr = instructions[i];

			if (IsBranchInstruction(instr.OpCode))
			{
				if (instr.Operand is Instruction targetInstr)
				{
					if (!instructionSet.Contains(targetInstr))
					{
						diagnostics.Add(
							new IlAnalysisDiagnostic(
								"AXM2007",
								DiagnosticSeverity.Error,
								$"Branch target at offset 0x{instr.Offset:X4} points outside the method.",
								method.FullName,
								instr.Offset
							)
						);
					}
					else
					{
						leaderOffsets.Add(targetInstr.Offset);
					}
				}
				else
				{
					diagnostics.Add(
						new IlAnalysisDiagnostic(
							"AXM2007",
							DiagnosticSeverity.Error,
							$"Branch instruction at offset 0x{instr.Offset:X4} does not have a valid Instruction target operand.",
							method.FullName,
							instr.Offset
						)
					);
				}

				// The instruction immediately following a branch is a leader (if within bounds)
				if (i + 1 < instructions.Count)
				{
					leaderOffsets.Add(instructions[i + 1].Offset);
				}
			}
			else if (instr.OpCode == OpCodes.Ret)
			{
				if (i + 1 < instructions.Count)
				{
					leaderOffsets.Add(instructions[i + 1].Offset);
				}
			}
		}

		// 3. Partition instructions into basic blocks
		var blockInstructions = new List<Instruction>();
		var blockIndex = 0;

		for (var i = 0; i < instructions.Count; i++)
		{
			var instr = instructions[i];

			// If current instruction is a leader (and not the first instruction of current block), flush current block
			if (blockInstructions.Count > 0 && leaderOffsets.Contains(instr.Offset))
			{
				var block = new IlBasicBlock(blockIndex++, blockInstructions);
				cfg._blocks.Add(block);
				cfg._blocksByStartOffset[block.StartOffset] = block;
				blockInstructions = new List<Instruction>();
			}

			blockInstructions.Add(instr);
		}

		if (blockInstructions.Count > 0)
		{
			var block = new IlBasicBlock(blockIndex++, blockInstructions);
			cfg._blocks.Add(block);
			cfg._blocksByStartOffset[block.StartOffset] = block;
		}

		// 4. Connect successors and predecessors
		for (var i = 0; i < cfg._blocks.Count; i++)
		{
			var block = cfg._blocks[i];
			var lastInstr = block.LastInstruction;

			if (IsUnconditionalBranch(lastInstr.OpCode))
			{
				if (
					lastInstr.Operand is Instruction targetInstr
					&& cfg._blocksByStartOffset.TryGetValue(targetInstr.Offset, out var targetBlock)
				)
				{
					Connect(block, targetBlock);
				}
			}
			else if (IsConditionalBranch(lastInstr.OpCode))
			{
				if (
					lastInstr.Operand is Instruction targetInstr
					&& cfg._blocksByStartOffset.TryGetValue(targetInstr.Offset, out var targetBlock)
				)
				{
					Connect(block, targetBlock);
				}

				// Fall-through to next block
				if (i + 1 < cfg._blocks.Count)
				{
					Connect(block, cfg._blocks[i + 1]);
				}
				else
				{
					diagnostics.Add(
						new IlAnalysisDiagnostic(
							"AXM2010",
							DiagnosticSeverity.Error,
							$"Block {block.Index} ends with conditional branch at end of method without fall-through block.",
							method.FullName,
							lastInstr.Offset
						)
					);
				}
			}
			else if (lastInstr.OpCode == OpCodes.Ret)
			{
				// No successors
			}
			else
			{
				// Sequential fall-through to next block
				if (i + 1 < cfg._blocks.Count)
				{
					Connect(block, cfg._blocks[i + 1]);
				}
				else
				{
					diagnostics.Add(
						new IlAnalysisDiagnostic(
							"AXM2010",
							DiagnosticSeverity.Error,
							$"Last instruction of method at offset 0x{lastInstr.Offset:X4} falls off without return or branch.",
							method.FullName,
							lastInstr.Offset
						)
					);
				}
			}
		}

		// 5. Mark reachability starting from EntryBlock
		if (cfg.EntryBlock is not null)
		{
			var reachableQueue = new Queue<IlBasicBlock>();
			cfg.EntryBlock.IsReachable = true;
			reachableQueue.Enqueue(cfg.EntryBlock);

			while (reachableQueue.Count > 0)
			{
				var current = reachableQueue.Dequeue();
				foreach (var succ in current.Successors)
				{
					if (!succ.IsReachable)
					{
						succ.IsReachable = true;
						reachableQueue.Enqueue(succ);
					}
				}
			}
		}

		return (cfg, diagnostics);
	}

	private static void Connect(IlBasicBlock from, IlBasicBlock to)
	{
		if (!from.Successors.Contains(to))
		{
			from.Successors.Add(to);
		}
		if (!to.Predecessors.Contains(from))
		{
			to.Predecessors.Add(from);
		}
	}

	public static bool IsBranchInstruction(OpCode opCode)
	{
		return IsUnconditionalBranch(opCode) || IsConditionalBranch(opCode);
	}

	public static bool IsUnconditionalBranch(OpCode opCode)
	{
		return opCode == OpCodes.Br || opCode == OpCodes.Br_S;
	}

	public static bool IsConditionalBranch(OpCode opCode)
	{
		return opCode == OpCodes.Brtrue
			|| opCode == OpCodes.Brtrue_S
			|| opCode == OpCodes.Brfalse
			|| opCode == OpCodes.Brfalse_S
			|| opCode == OpCodes.Beq
			|| opCode == OpCodes.Beq_S
			|| opCode == OpCodes.Bne_Un
			|| opCode == OpCodes.Bne_Un_S
			|| opCode == OpCodes.Bge
			|| opCode == OpCodes.Bge_S
			|| opCode == OpCodes.Bge_Un
			|| opCode == OpCodes.Bge_Un_S
			|| opCode == OpCodes.Bgt
			|| opCode == OpCodes.Bgt_S
			|| opCode == OpCodes.Bgt_Un
			|| opCode == OpCodes.Bgt_Un_S
			|| opCode == OpCodes.Ble
			|| opCode == OpCodes.Ble_S
			|| opCode == OpCodes.Ble_Un
			|| opCode == OpCodes.Ble_Un_S
			|| opCode == OpCodes.Blt
			|| opCode == OpCodes.Blt_S
			|| opCode == OpCodes.Blt_Un
			|| opCode == OpCodes.Blt_Un_S;
	}
}

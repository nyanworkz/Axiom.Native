using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Axiom.Native;

public static class LlvmIrGenerator
{
	private sealed record LlvmOperand(string Type, string Value);

	public static LlvmIrGenerationResult Generate(MethodAnalysisResult analysisResult)
	{
		ArgumentNullException.ThrowIfNull(analysisResult);

		var diagnostics = new List<LlvmIrDiagnostic>();

		if (!analysisResult.IsSupported || analysisResult.HasErrors)
		{
			diagnostics.Add(
				new LlvmIrDiagnostic(
					"AXM3001",
					DiagnosticSeverity.Error,
					$"Cannot generate LLVM IR: method analysis is not supported or contains errors ({analysisResult.SupportStatus}).",
					analysisResult.Method?.FullName ?? analysisResult.Reference?.FullName ?? "Unknown"
				)
			);
			return new LlvmIrGenerationResult(analysisResult.Method, null, diagnostics);
		}

		if (analysisResult.Method is null)
		{
			diagnostics.Add(
				new LlvmIrDiagnostic(
					"AXM3001",
					DiagnosticSeverity.Error,
					"Cannot generate LLVM IR: MethodDefinition is null.",
					analysisResult.Reference?.FullName ?? "Unknown"
				)
			);
			return new LlvmIrGenerationResult(null, null, diagnostics);
		}

		var method = analysisResult.Method;
		var cfg = analysisResult.Cfg;

		if (cfg is null || cfg.Blocks.Count == 0 || cfg.EntryBlock is null)
		{
			diagnostics.Add(
				new LlvmIrDiagnostic(
					"AXM3002",
					DiagnosticSeverity.Error,
					"Cannot generate LLVM IR: Method has no CFG or CFG has no basic blocks.",
					method.FullName
				)
			);
			return new LlvmIrGenerationResult(method, null, diagnostics);
		}

		try
		{
			return GenerateCore(method, cfg, diagnostics);
		}
		catch (Exception ex)
		{
			diagnostics.Add(
				new LlvmIrDiagnostic(
					"AXM3008",
					DiagnosticSeverity.Error,
					$"Internal error during LLVM IR generation: {ex.Message}",
					method.FullName
				)
			);
			return new LlvmIrGenerationResult(method, null, diagnostics);
		}
	}

	private static LlvmIrGenerationResult GenerateCore(
		MethodDefinition method,
		IlControlFlowGraph cfg,
		List<LlvmIrDiagnostic> diagnostics
	)
	{
		// 1. Validate Return Type
		if (!LlvmTypeHelper.TryGetTypeInfo(method.ReturnType, out var returnTypeInfo))
		{
			diagnostics.Add(
				new LlvmIrDiagnostic(
					"AXM3003",
					DiagnosticSeverity.Error,
					$"Unsupported return type '{method.ReturnType.FullName}' for LLVM IR generation.",
					method.FullName
				)
			);
			return new LlvmIrGenerationResult(method, null, diagnostics);
		}

		// 2. Validate Parameter Types
		var parameterInfos = new List<LlvmTypeInfo>();
		for (var i = 0; i < method.Parameters.Count; i++)
		{
			var param = method.Parameters[i];
			if (!LlvmTypeHelper.TryGetTypeInfo(param.ParameterType, out var paramInfo))
			{
				diagnostics.Add(
					new LlvmIrDiagnostic(
						"AXM3003",
						DiagnosticSeverity.Error,
						$"Unsupported parameter type '{param.ParameterType.FullName}' for parameter {i} ('{param.Name}').",
						method.FullName
					)
				);
				return new LlvmIrGenerationResult(method, null, diagnostics);
			}
			parameterInfos.Add(paramInfo);
		}

		// 3. Validate Local Variable Types
		var localInfos = new List<LlvmTypeInfo>();
		if (method.Body.HasVariables)
		{
			for (var i = 0; i < method.Body.Variables.Count; i++)
			{
				var variable = method.Body.Variables[i];
				if (!LlvmTypeHelper.TryGetTypeInfo(variable.VariableType, out var localInfo))
				{
					diagnostics.Add(
						new LlvmIrDiagnostic(
							"AXM3003",
							DiagnosticSeverity.Error,
							$"Unsupported local variable type '{variable.VariableType.FullName}' at index {i}.",
							method.FullName
						)
					);
					return new LlvmIrGenerationResult(method, null, diagnostics);
				}
				localInfos.Add(localInfo);
			}
		}

		var sb = new StringBuilder();
		var tempVarCounter = 0;
		string NextTemp() => $"%v{tempVarCounter++}";

		sb.AppendLine("; ModuleID = 'Axiom.Native'");
		sb.AppendLine("source_filename = \"Axiom.Native\"");
		sb.AppendLine();

		// Function signature
		var paramList = new List<string>();
		var hasThis = method.HasThis && !method.IsStatic;
		if (hasThis)
		{
			paramList.Add("ptr %this");
		}
		for (var i = 0; i < parameterInfos.Count; i++)
		{
			var argIndex = hasThis ? i + 1 : i;
			paramList.Add($"{parameterInfos[i].LlvmName} %arg_{argIndex}");
		}

		var functionName = method.FullName;
		sb.Append($"define {returnTypeInfo.LlvmName} @\"{functionName}\"({string.Join(", ", paramList)}) ");
		sb.AppendLine("{");

		// Entry Block: Allocate local variables and branch to block_0
		sb.AppendLine("entry:");
		if (localInfos.Count > 0)
		{
			for (var i = 0; i < localInfos.Count; i++)
			{
				var info = localInfos[i];
				sb.AppendLine($"  %loc_{i} = alloca {info.LlvmName}, align {info.Alignment}");
				sb.AppendLine($"  store {info.LlvmName} 0, ptr %loc_{i}, align {info.Alignment}");
			}
		}
		sb.AppendLine($"  br label %block_{cfg.EntryBlock!.Index}");
		sb.AppendLine();

		// State for block exit stack values to construct phi nodes
		var blockExitValues = new Dictionary<int, IReadOnlyList<LlvmOperand>>();

		// Emit reachable basic blocks
		foreach (var block in cfg.Blocks)
		{
			if (!block.IsReachable)
			{
				continue;
			}

			sb.AppendLine($"block_{block.Index}:");
			var currentStack = new List<LlvmOperand>();

			// Phi nodes for entry stack values if block has incoming stack elements
			if (block.EntryStack is not null && block.EntryStack.Count > 0)
			{
				for (var k = 0; k < block.EntryStack.Count; k++)
				{
					var stackType = LlvmTypeHelper.ToLlvmStackType(block.EntryStack[k]);
					var phiEntries = new List<string>();

					foreach (var pred in block.Predecessors)
					{
						if (!pred.IsReachable)
						{
							continue;
						}

						if (
							blockExitValues.TryGetValue(pred.Index, out var predExitStack)
							&& k < predExitStack.Count
						)
						{
							var operand = predExitStack[k];
							phiEntries.Add($"[ {operand.Value}, %block_{pred.Index} ]");
						}
						else
						{
							diagnostics.Add(
								new LlvmIrDiagnostic(
									"AXM3007",
									DiagnosticSeverity.Error,
									$"Cannot reconcile stack phi node for Block {block.Index} slot {k} because predecessor Block {pred.Index} exit stack is missing.",
									method.FullName,
									block.FirstInstruction.Offset,
									block.Index
								)
							);
							return new LlvmIrGenerationResult(method, null, diagnostics);
						}
					}

					var phiVar = $"%phi_{block.Index}_{k}";
					sb.AppendLine($"  {phiVar} = phi {stackType} {string.Join(", ", phiEntries)}");
					currentStack.Add(new LlvmOperand(stackType, phiVar));
				}
			}

			var blockTerminated = false;

			for (var instrIdx = 0; instrIdx < block.Instructions.Count; instrIdx++)
			{
				var instr = block.Instructions[instrIdx];
				var op = instr.OpCode;

				if (op == OpCodes.Nop)
				{
					continue;
				}

				// --- Constants ---
				if (IsLdcI4(op))
				{
					var val = GetLdcI4Value(instr);
					currentStack.Add(new LlvmOperand("i32", val.ToString(CultureInfo.InvariantCulture)));
				}
				else if (op == OpCodes.Ldc_I8)
				{
					var val = (long)instr.Operand;
					currentStack.Add(new LlvmOperand("i64", val.ToString(CultureInfo.InvariantCulture)));
				}
				// --- Argument load ---
				else if (IsLdarg(op))
				{
					var argIndex = GetIlArgIndex(instr, method);
					if (hasThis && argIndex == 0)
					{
						diagnostics.Add(
							new LlvmIrDiagnostic(
								"AXM3004",
								DiagnosticSeverity.Error,
								"Unsupported access to instance pointer 'this'.",
								method.FullName,
								instr.Offset,
								block.Index
							)
						);
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}

					var paramIndex = hasThis ? argIndex - 1 : argIndex;
					var paramInfo = parameterInfos[paramIndex];
					var rawArg = $"%arg_{argIndex}";

					if (paramInfo.BitWidth == 32)
					{
						currentStack.Add(new LlvmOperand("i32", rawArg));
					}
					else if (paramInfo.BitWidth == 64)
					{
						currentStack.Add(new LlvmOperand("i64", rawArg));
					}
					else
					{
						var extVar = NextTemp();
						var extOp = paramInfo.IsSigned ? "sext" : "zext";
						sb.AppendLine($"  {extVar} = {extOp} {paramInfo.LlvmName} {rawArg} to i32");
						currentStack.Add(new LlvmOperand("i32", extVar));
					}
				}
				// --- Local load ---
				else if (IsLdloc(op))
				{
					var locIndex = GetLocalIndex(instr);
					var localInfo = localInfos[locIndex];
					var loadVar = NextTemp();
					sb.AppendLine($"  {loadVar} = load {localInfo.LlvmName}, ptr %loc_{locIndex}, align {localInfo.Alignment}");

					if (localInfo.BitWidth == 32)
					{
						currentStack.Add(new LlvmOperand("i32", loadVar));
					}
					else if (localInfo.BitWidth == 64)
					{
						currentStack.Add(new LlvmOperand("i64", loadVar));
					}
					else
					{
						var extVar = NextTemp();
						var extOp = localInfo.IsSigned ? "sext" : "zext";
						sb.AppendLine($"  {extVar} = {extOp} {localInfo.LlvmName} {loadVar} to i32");
						currentStack.Add(new LlvmOperand("i32", extVar));
					}
				}
				// --- Local store ---
				else if (IsStloc(op))
				{
					if (currentStack.Count == 0)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, "Stack underflow at stloc.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}

					var valOp = Pop(currentStack);
					var locIndex = GetLocalIndex(instr);
					var localInfo = localInfos[locIndex];

					if (localInfo.BitWidth < 32)
					{
						var truncVar = NextTemp();
						sb.AppendLine($"  {truncVar} = trunc i32 {valOp.Value} to {localInfo.LlvmName}");
						sb.AppendLine($"  store {localInfo.LlvmName} {truncVar}, ptr %loc_{locIndex}, align {localInfo.Alignment}");
					}
					else
					{
						sb.AppendLine($"  store {localInfo.LlvmName} {valOp.Value}, ptr %loc_{locIndex}, align {localInfo.Alignment}");
					}
				}
				// --- Stack operations ---
				else if (op == OpCodes.Dup)
				{
					if (currentStack.Count == 0)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, "Stack underflow at dup.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}
					var top = currentStack[^1];
					currentStack.Add(top);
				}
				else if (op == OpCodes.Pop)
				{
					if (currentStack.Count == 0)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, "Stack underflow at pop.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}
					Pop(currentStack);
				}
				// --- Arithmetic operations ---
				else if (IsBinaryArithmetic(op))
				{
					if (currentStack.Count < 2)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, $"Stack underflow at {op.Name}.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}

					var rhs = Pop(currentStack);
					var lhs = Pop(currentStack);
					var llvmInst = GetBinaryArithmeticOp(op);
					var resVar = NextTemp();

					sb.AppendLine($"  {resVar} = {llvmInst} {lhs.Type} {lhs.Value}, {rhs.Value}");
					currentStack.Add(new LlvmOperand(lhs.Type, resVar));
				}
				else if (op == OpCodes.Neg)
				{
					if (currentStack.Count < 1)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, "Stack underflow at neg.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}
					var val = Pop(currentStack);
					var resVar = NextTemp();
					sb.AppendLine($"  {resVar} = sub {val.Type} 0, {val.Value}");
					currentStack.Add(new LlvmOperand(val.Type, resVar));
				}
				else if (op == OpCodes.Not)
				{
					if (currentStack.Count < 1)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, "Stack underflow at not.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}
					var val = Pop(currentStack);
					var resVar = NextTemp();
					sb.AppendLine($"  {resVar} = xor {val.Type} {val.Value}, -1");
					currentStack.Add(new LlvmOperand(val.Type, resVar));
				}
				// --- Bitwise shifts ---
				else if (op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un)
				{
					if (currentStack.Count < 2)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, $"Stack underflow at {op.Name}.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}

					var shift = Pop(currentStack);
					var val = Pop(currentStack);
					var resVar = NextTemp();
					var shiftInst = op == OpCodes.Shl ? "shl" : (op == OpCodes.Shr ? "ashr" : "lshr");

					if (val.Type == "i32")
					{
						var maskVar = NextTemp();
						sb.AppendLine($"  {maskVar} = and i32 {shift.Value}, 31");
						sb.AppendLine($"  {resVar} = {shiftInst} i32 {val.Value}, {maskVar}");
						currentStack.Add(new LlvmOperand("i32", resVar));
					}
					else
					{
						// val.Type == "i64"
						var extShiftVar = NextTemp();
						if (shift.Type == "i32")
						{
							sb.AppendLine($"  {extShiftVar} = zext i32 {shift.Value} to i64");
						}
						else
						{
							extShiftVar = shift.Value;
						}
						var maskVar = NextTemp();
						sb.AppendLine($"  {maskVar} = and i64 {extShiftVar}, 63");
						sb.AppendLine($"  {resVar} = {shiftInst} i64 {val.Value}, {maskVar}");
						currentStack.Add(new LlvmOperand("i64", resVar));
					}
				}
				// --- Conversions ---
				else if (op == OpCodes.Conv_I4 || op == OpCodes.Conv_U4)
				{
					if (currentStack.Count < 1)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, $"Stack underflow at {op.Name}.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}
					var val = Pop(currentStack);
					if (val.Type == "i64")
					{
						var truncVar = NextTemp();
						sb.AppendLine($"  {truncVar} = trunc i64 {val.Value} to i32");
						currentStack.Add(new LlvmOperand("i32", truncVar));
					}
					else
					{
						currentStack.Add(val);
					}
				}
				else if (op == OpCodes.Conv_I8)
				{
					if (currentStack.Count < 1)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, "Stack underflow at conv.i8.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}
					var val = Pop(currentStack);
					if (val.Type == "i32")
					{
						var extVar = NextTemp();
						sb.AppendLine($"  {extVar} = sext i32 {val.Value} to i64");
						currentStack.Add(new LlvmOperand("i64", extVar));
					}
					else
					{
						currentStack.Add(val);
					}
				}
				else if (op == OpCodes.Conv_U8)
				{
					if (currentStack.Count < 1)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, "Stack underflow at conv.u8.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}
					var val = Pop(currentStack);
					if (val.Type == "i32")
					{
						var extVar = NextTemp();
						sb.AppendLine($"  {extVar} = zext i32 {val.Value} to i64");
						currentStack.Add(new LlvmOperand("i64", extVar));
					}
					else
					{
						currentStack.Add(val);
					}
				}
				// --- Comparisons ---
				else if (IsComparison(op))
				{
					if (currentStack.Count < 2)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, $"Stack underflow at {op.Name}.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}
					var rhs = Pop(currentStack);
					var lhs = Pop(currentStack);
					var pred = GetComparisonPredicate(op);
					var cmpVar = NextTemp();
					var extVar = NextTemp();

					sb.AppendLine($"  {cmpVar} = icmp {pred} {lhs.Type} {lhs.Value}, {rhs.Value}");
					sb.AppendLine($"  {extVar} = zext i1 {cmpVar} to i32");
					currentStack.Add(new LlvmOperand("i32", extVar));
				}
				// --- Branches ---
				else if (op == OpCodes.Br || op == OpCodes.Br_S)
				{
					var targetBlock = ResolveBranchTarget(instr, cfg);
					if (targetBlock is null)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3006", DiagnosticSeverity.Error, $"Cannot resolve branch target at 0x{instr.Offset:X4}.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}

					sb.AppendLine($"  br label %block_{targetBlock.Index}");
					blockTerminated = true;
				}
				else if (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S || op == OpCodes.Brfalse || op == OpCodes.Brfalse_S)
				{
					if (currentStack.Count < 1)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, $"Stack underflow at {op.Name}.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}
					var condVal = Pop(currentStack);
					var targetBlock = ResolveBranchTarget(instr, cfg);
					var fallthroughBlock = ResolveFallthroughBlock(block, cfg);

					if (targetBlock is null || fallthroughBlock is null)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3006", DiagnosticSeverity.Error, $"Cannot resolve branch targets at 0x{instr.Offset:X4}.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}

					var cmpVar = NextTemp();
					var cmpPred = (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S) ? "ne" : "eq";
					sb.AppendLine($"  {cmpVar} = icmp {cmpPred} {condVal.Type} {condVal.Value}, 0");
					sb.AppendLine($"  br i1 {cmpVar}, label %block_{targetBlock.Index}, label %block_{fallthroughBlock.Index}");
					blockTerminated = true;
				}
				else if (IsBinaryConditionalBranch(op))
				{
					if (currentStack.Count < 2)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, $"Stack underflow at {op.Name}.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}

					var rhs = Pop(currentStack);
					var lhs = Pop(currentStack);
					var targetBlock = ResolveBranchTarget(instr, cfg);
					var fallthroughBlock = ResolveFallthroughBlock(block, cfg);

					if (targetBlock is null || fallthroughBlock is null)
					{
						diagnostics.Add(new LlvmIrDiagnostic("AXM3006", DiagnosticSeverity.Error, $"Cannot resolve branch targets at 0x{instr.Offset:X4}.", method.FullName, instr.Offset, block.Index));
						return new LlvmIrGenerationResult(method, null, diagnostics);
					}

					var pred = GetBinaryBranchPredicate(op);
					var cmpVar = NextTemp();
					sb.AppendLine($"  {cmpVar} = icmp {pred} {lhs.Type} {lhs.Value}, {rhs.Value}");
					sb.AppendLine($"  br i1 {cmpVar}, label %block_{targetBlock.Index}, label %block_{fallthroughBlock.Index}");
					blockTerminated = true;
				}
				// --- Return ---
				else if (op == OpCodes.Ret)
				{
					if (returnTypeInfo.IsVoid)
					{
						sb.AppendLine("  ret void");
					}
					else
					{
						if (currentStack.Count < 1)
						{
							diagnostics.Add(new LlvmIrDiagnostic("AXM3005", DiagnosticSeverity.Error, "Stack underflow at ret.", method.FullName, instr.Offset, block.Index));
							return new LlvmIrGenerationResult(method, null, diagnostics);
						}

						var retVal = Pop(currentStack);
						if (returnTypeInfo.BitWidth < 32)
						{
							var truncVar = NextTemp();
							sb.AppendLine($"  {truncVar} = trunc i32 {retVal.Value} to {returnTypeInfo.LlvmName}");
							sb.AppendLine($"  ret {returnTypeInfo.LlvmName} {truncVar}");
						}
						else
						{
							sb.AppendLine($"  ret {returnTypeInfo.LlvmName} {retVal.Value}");
						}
					}
					blockTerminated = true;
				}
				else
				{
					diagnostics.Add(
						new LlvmIrDiagnostic(
							"AXM3004",
							DiagnosticSeverity.Error,
							$"Unsupported instruction '{op.Name}' encountered during LLVM IR generation.",
							method.FullName,
							instr.Offset,
							block.Index
						)
					);
					return new LlvmIrGenerationResult(method, null, diagnostics);
				}
			}

			// If block has no terminator, add unconditional fallthrough
			if (!blockTerminated)
			{
				var fallthrough = ResolveFallthroughBlock(block, cfg);
				if (fallthrough is not null)
				{
					sb.AppendLine($"  br label %block_{fallthrough.Index}");
				}
				else
				{
					diagnostics.Add(
						new LlvmIrDiagnostic(
							"AXM3006",
							DiagnosticSeverity.Error,
							$"Block {block.Index} missing terminator instruction without fallthrough block.",
							method.FullName,
							block.LastInstruction.Offset,
							block.Index
						)
					);
					return new LlvmIrGenerationResult(method, null, diagnostics);
				}
			}

			blockExitValues[block.Index] = currentStack;
			sb.AppendLine();
		}

		sb.AppendLine("}");

		return new LlvmIrGenerationResult(method, sb.ToString(), diagnostics);
	}

	private static LlvmOperand Pop(List<LlvmOperand> stack)
	{
		var item = stack[^1];
		stack.RemoveAt(stack.Count - 1);
		return item;
	}

	private static IlBasicBlock? ResolveBranchTarget(Instruction instr, IlControlFlowGraph cfg)
	{
		if (instr.Operand is Instruction targetInstr)
		{
			return cfg.FindBlockByOffset(targetInstr.Offset);
		}
		return null;
	}

	private static IlBasicBlock? ResolveFallthroughBlock(IlBasicBlock currentBlock, IlControlFlowGraph cfg)
	{
		for (var i = 0; i < cfg.Blocks.Count; i++)
		{
			if (cfg.Blocks[i].Index == currentBlock.Index && i + 1 < cfg.Blocks.Count)
			{
				return cfg.Blocks[i + 1];
			}
		}
		return null;
	}

	private static bool IsLdcI4(OpCode op) =>
		op == OpCodes.Ldc_I4_M1
		|| op == OpCodes.Ldc_I4_0
		|| op == OpCodes.Ldc_I4_1
		|| op == OpCodes.Ldc_I4_2
		|| op == OpCodes.Ldc_I4_3
		|| op == OpCodes.Ldc_I4_4
		|| op == OpCodes.Ldc_I4_5
		|| op == OpCodes.Ldc_I4_6
		|| op == OpCodes.Ldc_I4_7
		|| op == OpCodes.Ldc_I4_8
		|| op == OpCodes.Ldc_I4_S
		|| op == OpCodes.Ldc_I4;

	private static int GetLdcI4Value(Instruction instr)
	{
		var op = instr.OpCode;
		if (op == OpCodes.Ldc_I4_M1) return -1;
		if (op == OpCodes.Ldc_I4_0) return 0;
		if (op == OpCodes.Ldc_I4_1) return 1;
		if (op == OpCodes.Ldc_I4_2) return 2;
		if (op == OpCodes.Ldc_I4_3) return 3;
		if (op == OpCodes.Ldc_I4_4) return 4;
		if (op == OpCodes.Ldc_I4_5) return 5;
		if (op == OpCodes.Ldc_I4_6) return 6;
		if (op == OpCodes.Ldc_I4_7) return 7;
		if (op == OpCodes.Ldc_I4_8) return 8;
		if (instr.Operand is sbyte sb) return sb;
		if (instr.Operand is byte b) return b;
		if (instr.Operand is int i) return i;
		return Convert.ToInt32(instr.Operand, CultureInfo.InvariantCulture);
	}

	private static bool IsLdarg(OpCode op) =>
		op == OpCodes.Ldarg_0
		|| op == OpCodes.Ldarg_1
		|| op == OpCodes.Ldarg_2
		|| op == OpCodes.Ldarg_3
		|| op == OpCodes.Ldarg_S
		|| op == OpCodes.Ldarg;

	private static int GetIlArgIndex(Instruction instr, MethodDefinition method)
	{
		var op = instr.OpCode;
		if (op == OpCodes.Ldarg_0) return 0;
		if (op == OpCodes.Ldarg_1) return 1;
		if (op == OpCodes.Ldarg_2) return 2;
		if (op == OpCodes.Ldarg_3) return 3;

		var hasThis = method.HasThis && !method.IsStatic;
		if (instr.Operand is ParameterDefinition param)
		{
			return hasThis ? param.Index + 1 : param.Index;
		}
		if (instr.Operand is int intIdx) return intIdx;
		if (instr.Operand is byte byteIdx) return byteIdx;
		if (instr.Operand is sbyte sbyteIdx) return sbyteIdx;

		return -1;
	}

	private static bool IsLdloc(OpCode op) =>
		op == OpCodes.Ldloc_0
		|| op == OpCodes.Ldloc_1
		|| op == OpCodes.Ldloc_2
		|| op == OpCodes.Ldloc_3
		|| op == OpCodes.Ldloc_S
		|| op == OpCodes.Ldloc;

	private static bool IsStloc(OpCode op) =>
		op == OpCodes.Stloc_0
		|| op == OpCodes.Stloc_1
		|| op == OpCodes.Stloc_2
		|| op == OpCodes.Stloc_3
		|| op == OpCodes.Stloc_S
		|| op == OpCodes.Stloc;

	private static int GetLocalIndex(Instruction instr)
	{
		var op = instr.OpCode;
		if (op == OpCodes.Ldloc_0 || op == OpCodes.Stloc_0) return 0;
		if (op == OpCodes.Ldloc_1 || op == OpCodes.Stloc_1) return 1;
		if (op == OpCodes.Ldloc_2 || op == OpCodes.Stloc_2) return 2;
		if (op == OpCodes.Ldloc_3 || op == OpCodes.Stloc_3) return 3;

		if (instr.Operand is VariableDefinition variable)
		{
			return variable.Index;
		}
		if (instr.Operand is int intIdx) return intIdx;
		if (instr.Operand is byte byteIdx) return byteIdx;
		if (instr.Operand is sbyte sbyteIdx) return sbyteIdx;

		return -1;
	}

	private static bool IsBinaryArithmetic(OpCode op) =>
		op == OpCodes.Add
		|| op == OpCodes.Sub
		|| op == OpCodes.Mul
		|| op == OpCodes.Div
		|| op == OpCodes.Div_Un
		|| op == OpCodes.Rem
		|| op == OpCodes.Rem_Un
		|| op == OpCodes.And
		|| op == OpCodes.Or
		|| op == OpCodes.Xor;

	private static string GetBinaryArithmeticOp(OpCode op)
	{
		if (op == OpCodes.Add) return "add";
		if (op == OpCodes.Sub) return "sub";
		if (op == OpCodes.Mul) return "mul";
		if (op == OpCodes.Div) return "sdiv";
		if (op == OpCodes.Div_Un) return "udiv";
		if (op == OpCodes.Rem) return "srem";
		if (op == OpCodes.Rem_Un) return "urem";
		if (op == OpCodes.And) return "and";
		if (op == OpCodes.Or) return "or";
		if (op == OpCodes.Xor) return "xor";
		throw new ArgumentOutOfRangeException(nameof(op), op, "Unexpected binary opcode.");
	}

	private static bool IsComparison(OpCode op) =>
		op == OpCodes.Ceq
		|| op == OpCodes.Cgt
		|| op == OpCodes.Cgt_Un
		|| op == OpCodes.Clt
		|| op == OpCodes.Clt_Un;

	private static string GetComparisonPredicate(OpCode op)
	{
		if (op == OpCodes.Ceq) return "eq";
		if (op == OpCodes.Cgt) return "sgt";
		if (op == OpCodes.Cgt_Un) return "ugt";
		if (op == OpCodes.Clt) return "slt";
		if (op == OpCodes.Clt_Un) return "ult";
		throw new ArgumentOutOfRangeException(nameof(op), op, "Unexpected comparison opcode.");
	}

	private static bool IsBinaryConditionalBranch(OpCode op) =>
		op == OpCodes.Beq || op == OpCodes.Beq_S
		|| op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S
		|| op == OpCodes.Bge || op == OpCodes.Bge_S
		|| op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S
		|| op == OpCodes.Bgt || op == OpCodes.Bgt_S
		|| op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S
		|| op == OpCodes.Ble || op == OpCodes.Ble_S
		|| op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S
		|| op == OpCodes.Blt || op == OpCodes.Blt_S
		|| op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S;

	private static string GetBinaryBranchPredicate(OpCode op)
	{
		if (op == OpCodes.Beq || op == OpCodes.Beq_S) return "eq";
		if (op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S) return "ne";
		if (op == OpCodes.Bge || op == OpCodes.Bge_S) return "sge";
		if (op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S) return "uge";
		if (op == OpCodes.Bgt || op == OpCodes.Bgt_S) return "sgt";
		if (op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S) return "ugt";
		if (op == OpCodes.Ble || op == OpCodes.Ble_S) return "sle";
		if (op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S) return "ule";
		if (op == OpCodes.Blt || op == OpCodes.Blt_S) return "slt";
		if (op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S) return "ult";
		throw new ArgumentOutOfRangeException(nameof(op), op, "Unexpected conditional branch opcode.");
	}
}


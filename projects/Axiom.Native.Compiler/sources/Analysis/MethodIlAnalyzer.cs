using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Axiom.Native;

public static class MethodIlAnalyzer
{
	public static MethodAnalysisResult Analyze(MethodDependencyNode node)
	{
		ArgumentNullException.ThrowIfNull(node);

		if (node.Definition is null)
		{
			var diag = new IlAnalysisDiagnostic(
				"AXM2010",
				DiagnosticSeverity.Error,
				$"Cannot analyze node '{node.Key}' because its MethodDefinition is unresolved.",
				node.Key
			);
			return new MethodAnalysisResult(
				null,
				IlAnalysisSupportStatus.InvalidMetadata,
				null,
				new[] { diag },
				node.Reference
			);
		}

		return Analyze(node.Definition);
	}

	public static IReadOnlyDictionary<string, MethodAnalysisResult> Analyze(
		MethodDependencyGraph graph
	)
	{
		ArgumentNullException.ThrowIfNull(graph);

		var results = new Dictionary<string, MethodAnalysisResult>(StringComparer.Ordinal);
		foreach (var node in graph.Nodes)
		{
			results[node.Key] = Analyze(node);
		}

		return results;
	}

	public static MethodAnalysisResult Analyze(MethodDefinition method)
	{
		ArgumentNullException.ThrowIfNull(method);

		var diagnosticsSet = new HashSet<IlAnalysisDiagnostic>();

		// 1. Check for IL body
		if (!method.HasBody)
		{
			diagnosticsSet.Add(
				new IlAnalysisDiagnostic(
					"AXM2010",
					DiagnosticSeverity.Error,
					"Method has no IL body to analyze.",
					method.FullName
				)
			);
			return BuildResult(method, null, diagnosticsSet);
		}

		// 2. Check for exception handlers
		if (method.Body.HasExceptionHandlers && method.Body.ExceptionHandlers.Count > 0)
		{
			diagnosticsSet.Add(
				new IlAnalysisDiagnostic(
					"AXM2003",
					DiagnosticSeverity.Error,
					$"Method has {method.Body.ExceptionHandlers.Count} exception handling clause(s), which are not supported.",
					method.FullName
				)
			);
		}

		// 3. Validate signature (parameters & return type) and generics
		if (method.HasGenericParameters || method.DeclaringType.HasGenericParameters)
		{
			diagnosticsSet.Add(
				new IlAnalysisDiagnostic(
					"AXM2002",
					DiagnosticSeverity.Error,
					"Generic methods and methods in generic types are not supported in IL analysis.",
					method.FullName
				)
			);
		}

		if (
			!TryClassifyType(method.ReturnType, out var returnTypeCategory)
			|| returnTypeCategory == TypeCategory.Unsupported
		)
		{
			diagnosticsSet.Add(
				new IlAnalysisDiagnostic(
					"AXM2002",
					DiagnosticSeverity.Error,
					$"Return type '{method.ReturnType.FullName}' is not supported in the current IL analysis subset.",
					method.FullName
				)
			);
		}

		for (var p = 0; p < method.Parameters.Count; p++)
		{
			var param = method.Parameters[p];
			if (
				!TryClassifyType(param.ParameterType, out var paramCategory)
				|| paramCategory == TypeCategory.Unsupported
				|| paramCategory == TypeCategory.Void
			)
			{
				diagnosticsSet.Add(
					new IlAnalysisDiagnostic(
						"AXM2002",
						DiagnosticSeverity.Error,
						$"Parameter '{param.Name}' has unsupported type '{param.ParameterType.FullName}'.",
						method.FullName
					)
				);
			}
		}

		// 4. Validate local variables
		if (method.Body.HasVariables)
		{
			for (var v = 0; v < method.Body.Variables.Count; v++)
			{
				var variable = method.Body.Variables[v];
				if (
					!TryClassifyType(variable.VariableType, out var varCategory)
					|| varCategory == TypeCategory.Unsupported
					|| varCategory == TypeCategory.Void
				)
				{
					diagnosticsSet.Add(
						new IlAnalysisDiagnostic(
							"AXM2002",
							DiagnosticSeverity.Error,
							$"Local variable V_{v} has unsupported type '{variable.VariableType.FullName}'.",
							method.FullName
						)
					);
				}
			}
		}

		// 5. Build and validate CFG
		var (cfg, cfgDiagnostics) = IlControlFlowGraph.BuildAndValidate(method);
		foreach (var diag in cfgDiagnostics)
		{
			diagnosticsSet.Add(diag);
		}

		if (cfg is null || cfg.Blocks.Count == 0)
		{
			return BuildResult(method, cfg, diagnosticsSet);
		}

		// 6. Validate instructions across all blocks (including unreachable)
		foreach (var block in cfg.Blocks)
		{
			foreach (var instr in block.Instructions)
			{
				ValidateInstructionSyntax(method, instr, diagnosticsSet);
			}
		}

		// 7. Worklist simulation of evaluation stack across reachable blocks
		SimulateEvaluationStack(method, cfg, returnTypeCategory, diagnosticsSet);

		return BuildResult(method, cfg, diagnosticsSet);
	}

	private static void ValidateInstructionSyntax(
		MethodDefinition method,
		Instruction instr,
		HashSet<IlAnalysisDiagnostic> diagnostics
	)
	{
		var opCode = instr.OpCode;

		if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj)
		{
			diagnostics.Add(
				new IlAnalysisDiagnostic(
					"AXM2011",
					DiagnosticSeverity.Error,
					$"Instruction '{opCode.Name}' is a method call, which is not supported in the current IL analysis subset.",
					method.FullName,
					instr.Offset
				)
			);
			return;
		}

		if (!IsSupportedInstruction(opCode))
		{
			diagnostics.Add(
				new IlAnalysisDiagnostic(
					"AXM2001",
					DiagnosticSeverity.Error,
					$"Instruction '{opCode.Name}' is outside the supported IL subset.",
					method.FullName,
					instr.Offset
				)
			);
			return;
		}

		// Validate operands of supported instructions
		if (IsLdarg(opCode))
		{
			var hasThis = method.HasThis && !method.IsStatic;
			var totalArgs = method.Parameters.Count + (hasThis ? 1 : 0);
			var ilArgIndex = GetIlArgIndex(instr, method);

			if (ilArgIndex < 0 || ilArgIndex >= totalArgs)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2009",
						DiagnosticSeverity.Error,
						$"Argument index {ilArgIndex} is out of bounds (total IL arguments: {totalArgs}).",
						method.FullName,
						instr.Offset
					)
				);
			}
		}
		else if (IsLocalAccess(opCode))
		{
			var locIndex = GetLocalIndex(instr);
			var totalLocals = method.Body.Variables.Count;
			if (locIndex < 0 || locIndex >= totalLocals)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2009",
						DiagnosticSeverity.Error,
						$"Local variable index {locIndex} is out of bounds (total locals: {totalLocals}).",
						method.FullName,
						instr.Offset
					)
				);
			}
		}
	}

	private static void SimulateEvaluationStack(
		MethodDefinition method,
		IlControlFlowGraph cfg,
		TypeCategory expectedReturnCategory,
		HashSet<IlAnalysisDiagnostic> diagnostics
	)
	{
		if (cfg.EntryBlock is null)
		{
			return;
		}

		var hasConflict = new HashSet<int>();
		cfg.EntryBlock.EntryStack = new List<IlStackType>();
		var worklist = new Queue<IlBasicBlock>();
		worklist.Enqueue(cfg.EntryBlock);

		while (worklist.Count > 0)
		{
			var block = worklist.Dequeue();
			if (block.EntryStack is null || hasConflict.Contains(block.Index))
			{
				continue;
			}

			var currentStack = new List<IlStackType>(block.EntryStack);
			var blockHalted = false;

			foreach (var instr in block.Instructions)
			{
				if (
					!SimulateInstruction(
						method,
						instr,
						currentStack,
						expectedReturnCategory,
						diagnostics
					)
				)
				{
					blockHalted = true;
					break;
				}
			}

			if (blockHalted)
			{
				continue;
			}

			block.ExitStack = currentStack;

			// Propagate exit stack to reachable successors
			foreach (var succ in block.Successors)
			{
				if (!succ.IsReachable || hasConflict.Contains(succ.Index))
				{
					continue;
				}

				if (succ.EntryStack is null)
				{
					succ.EntryStack = new List<IlStackType>(block.ExitStack);
					worklist.Enqueue(succ);
				}
				else
				{
					// Check merge compatibility
					var mismatch = false;
					if (succ.EntryStack.Count != block.ExitStack.Count)
					{
						mismatch = true;
						diagnostics.Add(
							new IlAnalysisDiagnostic(
								"AXM2006",
								DiagnosticSeverity.Error,
								$"Stack height mismatch at merge point into Block {succ.Index} (incoming {block.ExitStack.Count}, expected {succ.EntryStack.Count}).",
								method.FullName,
								succ.FirstInstruction.Offset
							)
						);
					}
					else
					{
						for (var i = 0; i < succ.EntryStack.Count; i++)
						{
							if (succ.EntryStack[i] != block.ExitStack[i])
							{
								mismatch = true;
								diagnostics.Add(
									new IlAnalysisDiagnostic(
										"AXM2006",
										DiagnosticSeverity.Error,
										$"Stack type mismatch at position {i} entering Block {succ.Index} (incoming {block.ExitStack[i]}, expected {succ.EntryStack[i]}).",
										method.FullName,
										succ.FirstInstruction.Offset
									)
								);
								break;
							}
						}
					}

					if (mismatch)
					{
						hasConflict.Add(succ.Index);
					}
				}
			}
		}
	}

	private static bool SimulateInstruction(
		MethodDefinition method,
		Instruction instr,
		List<IlStackType> stack,
		TypeCategory expectedReturnCategory,
		HashSet<IlAnalysisDiagnostic> diagnostics
	)
	{
		var opCode = instr.OpCode;

		// 1. Nop
		if (opCode == OpCodes.Nop)
		{
			return true;
		}

		// 2. Constants
		if (IsLdcI4(opCode))
		{
			stack.Add(IlStackType.I4);
			return true;
		}
		if (opCode == OpCodes.Ldc_I8)
		{
			stack.Add(IlStackType.I8);
			return true;
		}

		// 3. Arguments
		if (IsLdarg(opCode))
		{
			var hasThis = method.HasThis && !method.IsStatic;
			var totalArgs = method.Parameters.Count + (hasThis ? 1 : 0);
			var ilArgIndex = GetIlArgIndex(instr, method);

			if (ilArgIndex < 0 || ilArgIndex >= totalArgs)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2009",
						DiagnosticSeverity.Error,
						$"Argument index {ilArgIndex} is out of bounds (total IL arguments: {totalArgs}).",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			if (hasThis && ilArgIndex == 0)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2002",
						DiagnosticSeverity.Error,
						$"Argument 'this' has unsupported type '{method.DeclaringType.FullName}' in IL analysis.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			var paramIndex = hasThis ? ilArgIndex - 1 : ilArgIndex;
			var param = method.Parameters[paramIndex];

			if (TryClassifyType(param.ParameterType, out var cat))
			{
				if (cat == TypeCategory.I8)
				{
					stack.Add(IlStackType.I8);
					return true;
				}
				if (cat == TypeCategory.I4)
				{
					stack.Add(IlStackType.I4);
					return true;
				}
			}

			diagnostics.Add(
				new IlAnalysisDiagnostic(
					"AXM2002",
					DiagnosticSeverity.Error,
					$"Parameter '{param.Name}' has unsupported type '{param.ParameterType.FullName}'.",
					method.FullName,
					instr.Offset
				)
			);
			return false;
		}

		// 4. Locals
		if (IsLdloc(opCode))
		{
			var locIdx = GetLocalIndex(instr);
			if (locIdx < 0 || locIdx >= method.Body.Variables.Count)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2009",
						DiagnosticSeverity.Error,
						$"Local variable index {locIdx} is out of bounds (total locals: {method.Body.Variables.Count}).",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			var loc = method.Body.Variables[locIdx];
			if (TryClassifyType(loc.VariableType, out var cat))
			{
				if (cat == TypeCategory.I8)
				{
					stack.Add(IlStackType.I8);
					return true;
				}
				if (cat == TypeCategory.I4)
				{
					stack.Add(IlStackType.I4);
					return true;
				}
			}

			diagnostics.Add(
				new IlAnalysisDiagnostic(
					"AXM2002",
					DiagnosticSeverity.Error,
					$"Local variable V_{locIdx} has unsupported type '{loc.VariableType.FullName}'.",
					method.FullName,
					instr.Offset
				)
			);
			return false;
		}

		if (IsStloc(opCode))
		{
			if (stack.Count == 0)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						$"Evaluation stack underflow for instruction '{opCode.Name}'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			var val = Pop(stack);
			var locIdx = GetLocalIndex(instr);
			if (locIdx < 0 || locIdx >= method.Body.Variables.Count)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2009",
						DiagnosticSeverity.Error,
						$"Local variable index {locIdx} is out of bounds (total locals: {method.Body.Variables.Count}).",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			var loc = method.Body.Variables[locIdx];
			if (
				!TryClassifyType(loc.VariableType, out var expectedCat)
				|| expectedCat == TypeCategory.Unsupported
				|| expectedCat == TypeCategory.Void
			)
			{
				// Unsupported local type is already diagnosed with AXM2002; avoid emitting false AXM2005
				return false;
			}

			var expectedStackType =
				expectedCat == TypeCategory.I8 ? IlStackType.I8 : IlStackType.I4;
			if (val != expectedStackType)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2005",
						DiagnosticSeverity.Error,
						$"Cannot store stack value of type {val} into local variable V_{locIdx} of type '{loc.VariableType.FullName}'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			return true;
		}

		// 5. Stack manipulation
		if (opCode == OpCodes.Dup)
		{
			if (stack.Count == 0)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						"Evaluation stack underflow for instruction 'dup'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}
			stack.Add(stack[^1]);
			return true;
		}

		if (opCode == OpCodes.Pop)
		{
			if (stack.Count == 0)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						"Evaluation stack underflow for instruction 'pop'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}
			Pop(stack);
			return true;
		}

		// 6. Binary arithmetic & bitwise operations
		if (IsBinaryArithmeticOrBitwise(opCode))
		{
			if (stack.Count < 2)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						$"Evaluation stack underflow for binary instruction '{opCode.Name}'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			var b = Pop(stack);
			var a = Pop(stack);

			if (a != b)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2005",
						DiagnosticSeverity.Error,
						$"Binary instruction '{opCode.Name}' requires operands of matching type, but found {a} and {b}.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			stack.Add(a);
			return true;
		}

		// 7. Shift operations (shl, shr, shr.un)
		if (opCode == OpCodes.Shl || opCode == OpCodes.Shr || opCode == OpCodes.Shr_Un)
		{
			if (stack.Count < 2)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						$"Evaluation stack underflow for shift instruction '{opCode.Name}'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			var shiftAmount = Pop(stack);
			var val = Pop(stack);

			if (shiftAmount != IlStackType.I4)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2005",
						DiagnosticSeverity.Error,
						$"Shift amount for instruction '{opCode.Name}' must be I4, but found {shiftAmount}.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			if (val != IlStackType.I4 && val != IlStackType.I8)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2005",
						DiagnosticSeverity.Error,
						$"Shift value for instruction '{opCode.Name}' must be I4 or I8, but found {val}.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			stack.Add(val);
			return true;
		}

		// 8. Unary arithmetic (neg, not)
		if (opCode == OpCodes.Neg || opCode == OpCodes.Not)
		{
			if (stack.Count < 1)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						$"Evaluation stack underflow for unary instruction '{opCode.Name}'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			var val = Pop(stack);
			stack.Add(val);
			return true;
		}

		// 9. Conversions
		if (opCode == OpCodes.Conv_I4 || opCode == OpCodes.Conv_U4)
		{
			if (stack.Count < 1)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						$"Evaluation stack underflow for conversion instruction '{opCode.Name}'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}
			Pop(stack);
			stack.Add(IlStackType.I4);
			return true;
		}

		if (opCode == OpCodes.Conv_I8 || opCode == OpCodes.Conv_U8)
		{
			if (stack.Count < 1)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						$"Evaluation stack underflow for conversion instruction '{opCode.Name}'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}
			Pop(stack);
			stack.Add(IlStackType.I8);
			return true;
		}

		// 10. Comparisons (ceq, cgt, cgt.un, clt, clt.un)
		if (IsComparison(opCode))
		{
			if (stack.Count < 2)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						$"Evaluation stack underflow for comparison instruction '{opCode.Name}'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			var b = Pop(stack);
			var a = Pop(stack);

			if (a != b)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2005",
						DiagnosticSeverity.Error,
						$"Comparison instruction '{opCode.Name}' requires operands of matching type, but found {a} and {b}.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			stack.Add(IlStackType.I4);
			return true;
		}

		// 11. Unconditional branches (br, br.s)
		if (IlControlFlowGraph.IsUnconditionalBranch(opCode))
		{
			return true;
		}

		// 12. Conditional branches with 1 operand (brtrue, brfalse)
		if (
			opCode == OpCodes.Brtrue
			|| opCode == OpCodes.Brtrue_S
			|| opCode == OpCodes.Brfalse
			|| opCode == OpCodes.Brfalse_S
		)
		{
			if (stack.Count < 1)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						$"Evaluation stack underflow for conditional branch '{opCode.Name}'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			var cond = Pop(stack);
			if (cond != IlStackType.I4)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2005",
						DiagnosticSeverity.Error,
						$"Conditional branch '{opCode.Name}' requires I4 boolean condition, but found {cond}.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}
			return true;
		}

		// 13. Conditional branches with 2 operands (beq, bne.un, bge, bgt, ble, blt)
		if (IsBinaryConditionalBranch(opCode))
		{
			if (stack.Count < 2)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2004",
						DiagnosticSeverity.Error,
						$"Evaluation stack underflow for conditional branch '{opCode.Name}'.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}

			var b = Pop(stack);
			var a = Pop(stack);

			if (a != b)
			{
				diagnostics.Add(
					new IlAnalysisDiagnostic(
						"AXM2005",
						DiagnosticSeverity.Error,
						$"Conditional branch '{opCode.Name}' requires operands of matching type, but found {a} and {b}.",
						method.FullName,
						instr.Offset
					)
				);
				return false;
			}
			return true;
		}

		// 14. Return (ret)
		if (opCode == OpCodes.Ret)
		{
			if (expectedReturnCategory == TypeCategory.Void)
			{
				if (stack.Count != 0)
				{
					diagnostics.Add(
						new IlAnalysisDiagnostic(
							"AXM2008",
							DiagnosticSeverity.Error,
							$"Void method must have empty stack at 'ret', but stack height was {stack.Count}.",
							method.FullName,
							instr.Offset
						)
					);
					return false;
				}
			}
			else
			{
				if (stack.Count != 1)
				{
					diagnostics.Add(
						new IlAnalysisDiagnostic(
							"AXM2008",
							DiagnosticSeverity.Error,
							$"Method returning non-void must have exactly 1 item on stack at 'ret', but stack height was {stack.Count}.",
							method.FullName,
							instr.Offset
						)
					);
					return false;
				}

				var actualVal = stack[0];
				var expectedVal =
					expectedReturnCategory == TypeCategory.I8 ? IlStackType.I8 : IlStackType.I4;

				if (actualVal != expectedVal)
				{
					diagnostics.Add(
						new IlAnalysisDiagnostic(
							"AXM2008",
							DiagnosticSeverity.Error,
							$"Return value type mismatch: expected {expectedVal}, but found {actualVal}.",
							method.FullName,
							instr.Offset
						)
					);
					return false;
				}

				Pop(stack);
			}

			return true;
		}

		return true;
	}

	private static MethodAnalysisResult BuildResult(
		MethodDefinition method,
		IlControlFlowGraph? cfg,
		HashSet<IlAnalysisDiagnostic> diagnosticsSet
	)
	{
		var sortedDiagnostics = diagnosticsSet.OrderBy(d => d).ToList();
		var status = IlAnalysisSupportStatus.Supported;

		if (sortedDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
		{
			if (sortedDiagnostics.Any(d => d.Code == "AXM2003"))
			{
				status = IlAnalysisSupportStatus.UnsupportedExceptionHandling;
			}
			else if (sortedDiagnostics.Any(d => d.Code == "AXM2002"))
			{
				status = IlAnalysisSupportStatus.UnsupportedType;
			}
			else if (sortedDiagnostics.Any(d => d.Code == "AXM2011"))
			{
				status = IlAnalysisSupportStatus.UnsupportedMethodCall;
			}
			else if (sortedDiagnostics.Any(d => d.Code == "AXM2001"))
			{
				status = IlAnalysisSupportStatus.UnsupportedInstruction;
			}
			else if (
				sortedDiagnostics.Any(d =>
					d.Code == "AXM2004" || d.Code == "AXM2005" || d.Code == "AXM2006"
				)
			)
			{
				status = IlAnalysisSupportStatus.InvalidIlStack;
			}
			else if (
				sortedDiagnostics.Any(d =>
					d.Code == "AXM2007" || d.Code == "AXM2008" || d.Code == "AXM2010"
				)
			)
			{
				status = IlAnalysisSupportStatus.InvalidControlFlow;
			}
			else if (sortedDiagnostics.Any(d => d.Code == "AXM2009"))
			{
				status = IlAnalysisSupportStatus.InvalidMetadata;
			}
		}

		return new MethodAnalysisResult(method, status, cfg, sortedDiagnostics);
	}

	private static IlStackType Pop(List<IlStackType> stack)
	{
		var val = stack[^1];
		stack.RemoveAt(stack.Count - 1);
		return val;
	}

	private static bool TryClassifyType(TypeReference typeRef, out TypeCategory category)
	{
		category = TypeCategory.Unsupported;
		var fullName = typeRef.FullName;

		switch (fullName)
		{
			case "System.Void":
				category = TypeCategory.Void;
				return true;
			case "System.Boolean":
			case "System.Byte":
			case "System.SByte":
			case "System.Int16":
			case "System.UInt16":
			case "System.Char":
			case "System.Int32":
			case "System.UInt32":
				category = TypeCategory.I4;
				return true;
			case "System.Int64":
			case "System.UInt64":
				category = TypeCategory.I8;
				return true;
			default:
				return false;
		}
	}

	private static bool IsSupportedInstruction(OpCode op)
	{
		return op == OpCodes.Nop
			|| IsLdcI4(op)
			|| op == OpCodes.Ldc_I8
			|| IsLdarg(op)
			|| IsLdloc(op)
			|| IsStloc(op)
			|| op == OpCodes.Dup
			|| op == OpCodes.Pop
			|| IsBinaryArithmeticOrBitwise(op)
			|| op == OpCodes.Shl
			|| op == OpCodes.Shr
			|| op == OpCodes.Shr_Un
			|| op == OpCodes.Neg
			|| op == OpCodes.Not
			|| op == OpCodes.Conv_I4
			|| op == OpCodes.Conv_U4
			|| op == OpCodes.Conv_I8
			|| op == OpCodes.Conv_U8
			|| IsComparison(op)
			|| IlControlFlowGraph.IsBranchInstruction(op)
			|| op == OpCodes.Ret;
	}

	private static bool IsLdcI4(OpCode op)
	{
		return op == OpCodes.Ldc_I4_M1
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
	}

	private static bool IsLdarg(OpCode op)
	{
		return op == OpCodes.Ldarg_0
			|| op == OpCodes.Ldarg_1
			|| op == OpCodes.Ldarg_2
			|| op == OpCodes.Ldarg_3
			|| op == OpCodes.Ldarg_S
			|| op == OpCodes.Ldarg;
	}

	private static int GetIlArgIndex(Instruction instr, MethodDefinition method)
	{
		var op = instr.OpCode;
		if (op == OpCodes.Ldarg_0)
			return 0;
		if (op == OpCodes.Ldarg_1)
			return 1;
		if (op == OpCodes.Ldarg_2)
			return 2;
		if (op == OpCodes.Ldarg_3)
			return 3;

		var hasThis = method.HasThis && !method.IsStatic;

		if (instr.Operand is ParameterDefinition param)
		{
			return hasThis ? param.Index + 1 : param.Index;
		}
		if (instr.Operand is int intIdx)
			return intIdx;
		if (instr.Operand is byte byteIdx)
			return byteIdx;
		if (instr.Operand is sbyte sbyteIdx)
			return sbyteIdx;

		return -1;
	}

	private static bool IsLdloc(OpCode op)
	{
		return op == OpCodes.Ldloc_0
			|| op == OpCodes.Ldloc_1
			|| op == OpCodes.Ldloc_2
			|| op == OpCodes.Ldloc_3
			|| op == OpCodes.Ldloc_S
			|| op == OpCodes.Ldloc;
	}

	private static bool IsStloc(OpCode op)
	{
		return op == OpCodes.Stloc_0
			|| op == OpCodes.Stloc_1
			|| op == OpCodes.Stloc_2
			|| op == OpCodes.Stloc_3
			|| op == OpCodes.Stloc_S
			|| op == OpCodes.Stloc;
	}

	private static bool IsLocalAccess(OpCode op)
	{
		return IsLdloc(op) || IsStloc(op);
	}

	private static int GetLocalIndex(Instruction instr)
	{
		var op = instr.OpCode;
		if (op == OpCodes.Ldloc_0 || op == OpCodes.Stloc_0)
			return 0;
		if (op == OpCodes.Ldloc_1 || op == OpCodes.Stloc_1)
			return 1;
		if (op == OpCodes.Ldloc_2 || op == OpCodes.Stloc_2)
			return 2;
		if (op == OpCodes.Ldloc_3 || op == OpCodes.Stloc_3)
			return 3;

		if (instr.Operand is VariableDefinition variable)
		{
			return variable.Index;
		}
		if (instr.Operand is int intIdx)
			return intIdx;
		if (instr.Operand is byte byteIdx)
			return byteIdx;
		if (instr.Operand is sbyte sbyteIdx)
			return sbyteIdx;

		return -1;
	}

	private static bool IsBinaryArithmeticOrBitwise(OpCode op)
	{
		return op == OpCodes.Add
			|| op == OpCodes.Sub
			|| op == OpCodes.Mul
			|| op == OpCodes.Div
			|| op == OpCodes.Div_Un
			|| op == OpCodes.Rem
			|| op == OpCodes.Rem_Un
			|| op == OpCodes.And
			|| op == OpCodes.Or
			|| op == OpCodes.Xor;
	}

	private static bool IsComparison(OpCode op)
	{
		return op == OpCodes.Ceq
			|| op == OpCodes.Cgt
			|| op == OpCodes.Cgt_Un
			|| op == OpCodes.Clt
			|| op == OpCodes.Clt_Un;
	}

	private static bool IsBinaryConditionalBranch(OpCode op)
	{
		return op == OpCodes.Beq
			|| op == OpCodes.Beq_S
			|| op == OpCodes.Bne_Un
			|| op == OpCodes.Bne_Un_S
			|| op == OpCodes.Bge
			|| op == OpCodes.Bge_S
			|| op == OpCodes.Bge_Un
			|| op == OpCodes.Bge_Un_S
			|| op == OpCodes.Bgt
			|| op == OpCodes.Bgt_S
			|| op == OpCodes.Bgt_Un
			|| op == OpCodes.Bgt_Un_S
			|| op == OpCodes.Ble
			|| op == OpCodes.Ble_S
			|| op == OpCodes.Ble_Un
			|| op == OpCodes.Ble_Un_S
			|| op == OpCodes.Blt
			|| op == OpCodes.Blt_S
			|| op == OpCodes.Blt_Un
			|| op == OpCodes.Blt_Un_S;
	}

	private enum TypeCategory
	{
		Unsupported,
		Void,
		I4,
		I8,
	}
}

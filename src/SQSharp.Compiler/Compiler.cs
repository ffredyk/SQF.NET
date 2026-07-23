using System;
using System.Collections.Generic;
using SQSharp.Core;
using SQSharp.Language;

namespace SQSharp.Compiler;

/// <summary>
/// Compiles an SQ# AST into bytecode for execution by the stack VM.
/// Uses a single-pass recursive tree walk with basic optimizations.
/// </summary>
public class Compiler
{
    private BytecodeChunk _chunk = null!;
    private readonly Dictionary<string, int> _locals = new(StringComparer.OrdinalIgnoreCase);
    private int _localCount;
    private readonly Stack<int> _breakJumps = new(); // for loops: where to jump on break

    public BytecodeChunk Compile(AstNode ast, string? sourceFile = null)
    {
        _chunk = new BytecodeChunk { SourceFile = sourceFile };
        _locals.Clear();
        _localCount = 0;
        _breakJumps.Clear();

        CompileNode(ast);

        // Implicit return of last value
        _chunk.Emit(OpCode.Ret);
        _chunk.LocalCount = _localCount;
        return _chunk;
    }

    private void CompileNode(AstNode node)
    {
        switch (node)
        {
            case SequenceNode seq: CompileSequence(seq); break;
            case NilLiteralNode: EmitPushNil(); break;
            case BoolLiteralNode b: CompileBool(b); break;
            case NumberLiteralNode n: CompileNumber(n); break;
            case StringLiteralNode s: CompileString(s); break;
            case CodeLiteralNode c: CompileCodeLiteral(c); break;
            case VariableNode v: CompileVariable(v); break;
            case ArrayExprNode arr: CompileArray(arr); break;
            case UnaryCallNode u: CompileUnary(u); break;
            case BinaryCallNode b: CompileBinary(b); break;
            case AssignmentNode a: CompileAssignment(a); break;
            case IfThenElseNode i: CompileIf(i); break;
            case WhileDoNode w: CompileWhile(w); break;
            case ForFromToNode f: CompileForFromTo(f); break;
            case CallNode c: CompileCall(c); break;
            case SpawnNode s: CompileSpawn(s); break;
            case SharedDeclarationNode sd: CompileShared(sd); break;
            case TryCatchNode tc: CompileTryCatch(tc); break;
            default:
                throw new NotImplementedException($"Compilation of {node.NodeType} not yet implemented");
        }
    }

    // --- Literals ---
    private void EmitPushNil() => _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(SqValue.Nil));

    private void CompileBool(BoolLiteralNode n)
    {
        _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(new SqValue(n.Value)));
    }

    private void CompileNumber(NumberLiteralNode n)
    {
        _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(new SqValue(n.Value)));
    }

    private void CompileString(StringLiteralNode n)
    {
        _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(new SqValue(n.Value)));
    }

    private void CompileCodeLiteral(CodeLiteralNode node)
    {
        // Compile the body as a separate chunk
        var bodyAst = node.Body.Count == 1
            ? node.Body[0]
            : new SequenceNode(node.Body, node.Line, node.Column);

        var childCompiler = new Compiler();
        var childChunk = childCompiler.Compile(bodyAst, _chunk.SourceFile);
        int childIndex = _chunk.Children.Count;
        _chunk.Children.Add(childChunk);

        _chunk.Emit(OpCode.MakeCode, childIndex);
    }

    // --- Variables ---
    private void CompileVariable(VariableNode node)
    {
        if (node.IsLocal)
        {
            if (!_locals.TryGetValue(node.Name, out int slot))
            {
                // Auto-declare local on first use (SQF behavior)
                slot = _localCount++;
                _locals[node.Name] = slot;
            }
            _chunk.Emit(OpCode.PushLocal, slot);
        }
        else
        {
            // Global: resolved at runtime (could be nular command or global variable)
            int nameIdx = _chunk.AddGlobal(node.Name);
            _chunk.Emit(OpCode.PushGlobal, nameIdx);
        }
    }

    // --- Array ---
    private void CompileArray(ArrayExprNode node)
    {
        foreach (var elem in node.Elements)
            CompileNode(elem);
        _chunk.Emit(OpCode.MakeArray, node.Elements.Count);
    }

    // --- Operator calls ---
    private void CompileUnary(UnaryCallNode node)
    {
        CompileNode(node.Operand);
        if (string.Equals(node.Operator, "throw", StringComparison.OrdinalIgnoreCase))
        {
            _chunk.Emit(OpCode.Throw);
        }
        else
        {
            int cmdId = _chunk.AddGlobal(node.Operator);
            _chunk.Emit(OpCode.UnaryCall, cmdId);
        }
    }

    private void CompileBinary(BinaryCallNode node)
    {
        bool isAnd = string.Equals(node.Operator, "&&", StringComparison.OrdinalIgnoreCase);
        bool isOr = string.Equals(node.Operator, "||", StringComparison.OrdinalIgnoreCase);

        if (isAnd || isOr)
        {
            // Short-circuit: a && b  →  eval a, dup, JumpIfFalse skip, pop, eval b
            //                a || b  →  eval a, dup, JumpIfTrue skip, pop, eval b
            CompileNode(node.Left);
            _chunk.Emit(OpCode.Dup); // keep left value for result if short-circuit
            int jumpOver = _chunk.EmitPlaceholder(isAnd ? OpCode.JumpIfFalse : OpCode.JumpIfTrue);

            // Short-circuit took the jump — discard dup'd value, result is already on stack
            _chunk.Emit(OpCode.Pop);

            // Right side evaluation
            if (node.Right is CodeLiteralNode codeLit)
            {
                CompileNode(node.Right); // MakeCode
                _chunk.Emit(OpCode.Call, 0);
            }
            else
            {
                CompileNode(node.Right);
            }

            _chunk.PatchJump(jumpOver, _chunk.Count);
        }
        else
        {
            CompileNode(node.Left);
            CompileNode(node.Right);
            int cmdId = _chunk.AddGlobal(node.Operator);
            _chunk.Emit(OpCode.BinaryCall, cmdId);
        }
    }

    // --- Assignment ---
    private void CompileAssignment(AssignmentNode node)
    {
        CompileNode(node.Value);
        _chunk.Emit(OpCode.Dup); // assignment is an expression — leaves value on stack

        if (node.IsLocal)
        {
            if (!_locals.TryGetValue(node.VariableName, out int slot))
            {
                slot = _localCount++;
                _locals[node.VariableName] = slot;
            }
            _chunk.Emit(OpCode.StoreLocal, slot);
        }
        else
        {
            int nameIdx = _chunk.AddGlobal(node.VariableName);
            _chunk.Emit(OpCode.StoreGlobal, nameIdx);
        }
    }

    // --- Shared declaration ---
    private void CompileShared(SharedDeclarationNode node)
    {
        // shared _var = expr
        CompileNode(node.Initializer);     // push initial value
        _chunk.Emit(OpCode.MakeShared);    // pop value, wrap in SqSharedValue, push
        _chunk.Emit(OpCode.Dup);           // dup for expression result (shared _x = 5 evaluates to 5)

        if (node.IsLocal)
        {
            if (!_locals.TryGetValue(node.VariableName, out int slot))
            {
                slot = _localCount++;
                _locals[node.VariableName] = slot;
            }
            _chunk.Emit(OpCode.StoreLocal, slot);
        }
        else
        {
            int nameIdx = _chunk.AddGlobal(node.VariableName);
            _chunk.Emit(OpCode.StoreGlobal, nameIdx);
        }
    }

    // --- Control flow ---
    private void CompileIf(IfThenElseNode node)
    {
        CompileNode(node.Condition);
        int jumpToElse = _chunk.EmitPlaceholder(OpCode.JumpIfFalse);
        CompileBody(node.ThenBody);
        int jumpPastElse = -1;
        if (node.ElseBody != null)
            jumpPastElse = _chunk.EmitPlaceholder(OpCode.Jump);
        _chunk.PatchJump(jumpToElse, _chunk.Count);
        if (node.ElseBody != null)
        {
            CompileBody(node.ElseBody);
            _chunk.PatchJump(jumpPastElse, _chunk.Count);
        }
    }

    private void CompileWhile(WhileDoNode node)
    {
        int loopStart = _chunk.Count;

        // Condition
        CompileNode(node.Condition);
        int exitJump = _chunk.EmitPlaceholder(OpCode.JumpIfFalse);

        // Body
        _breakJumps.Push(-1);
        CompileBody(node.Body);

        // Pop the nil result from body (while returns nil, not last body value)
        _chunk.Emit(OpCode.Pop);
        _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(SqValue.Nil));

        // Loop back
        _chunk.Emit(OpCode.Jump, loopStart);
        _chunk.PatchJump(exitJump, _chunk.Count);

        _breakJumps.Pop();
    }

    private void CompileForFromTo(ForFromToNode node)
    {
        // Initialize: _var = from
        if (!_locals.ContainsKey(node.VarName))
        {
            _locals[node.VarName] = _localCount++;
        }
        int varSlot = _locals[node.VarName];

        CompileNode(node.From);
        _chunk.Emit(OpCode.StoreLocal, varSlot);
        _chunk.Emit(OpCode.Pop); // discard dup from assignment

        int loopStart = _chunk.Count;

        // Check condition: _var <= to
        _chunk.Emit(OpCode.PushLocal, varSlot);
        CompileNode(node.To);

        // Push 1 if step is positive, -1 if negative (simplification: always 1 for now)
        int condCmdId = _chunk.AddGlobal("<=");
        _chunk.Emit(OpCode.BinaryCall, condCmdId);

        int exitJump = _chunk.EmitPlaceholder(OpCode.JumpIfFalse);

        // Body
        CompileNode(node.Body);
        _chunk.Emit(OpCode.Pop); // discard body result

        // Increment: _var = _var + step
        _chunk.Emit(OpCode.PushLocal, varSlot);
        if (node.Step != null)
            CompileNode(node.Step);
        else
            _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(new SqValue(1.0)));
        int addCmdId = _chunk.AddGlobal("+");
        _chunk.Emit(OpCode.BinaryCall, addCmdId);
        _chunk.Emit(OpCode.StoreLocal, varSlot);
        _chunk.Emit(OpCode.Pop);

        // Loop back
        _chunk.Emit(OpCode.Jump, loopStart);
        _chunk.PatchJump(exitJump, _chunk.Count);

        // for returns nil
        _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(SqValue.Nil));
    }

    // --- call / spawn ---
    private void CompileCall(CallNode node)
    {
        // Push arguments (if any)
        int argCount = 0;
        if (node.Arguments != null)
        {
            CompileNode(node.Arguments);
            argCount = 1; // arguments are a single value (maybe array)
        }

        // Push code
        CompileNode(node.Code);

        _chunk.Emit(OpCode.Call, argCount);
    }

    private void CompileSpawn(SpawnNode node)
    {
        int argCount = 0;
        if (node.Arguments != null)
        {
            CompileNode(node.Arguments);
            argCount = 1;
        }

        CompileNode(node.Code);
        _chunk.Emit(OpCode.Spawn, argCount);
    }

    // --- try/catch ---
    private void CompileTryCatch(TryCatchNode node)
    {
        int handlerJump = _chunk.EmitPlaceholder(OpCode.TryBegin);

        // Try body — unwrap CodeLiteral to execute its body
        CompileBody(node.TryBody);

        // No error — skip catch
        int skipCatch = _chunk.EmitPlaceholder(OpCode.Jump);

        // Catch handler target
        _chunk.PatchJump(handlerJump, _chunk.Count);

        // If error variable, store error value
        if (node.ErrorVariable != null && node.ErrorVariable.StartsWith('_'))
        {
            if (!_locals.TryGetValue(node.ErrorVariable, out int evSlot))
            {
                evSlot = _localCount++;
                _locals[node.ErrorVariable] = evSlot;
            }
            _chunk.Emit(OpCode.StoreLocal, evSlot);
        }
        else
        {
            _chunk.Emit(OpCode.Pop);
        }

        // Catch body — unwrap CodeLiteral to execute
        CompileBody(node.CatchBody);

        _chunk.Emit(OpCode.TryEnd);
        _chunk.PatchJump(skipCatch, _chunk.Count);
    }

    /// <summary>Compile a body node, unwrapping CodeLiteral to execute its inner expressions.</summary>
    private void CompileBody(AstNode body)
    {
        if (body is CodeLiteralNode codeLit)
        {
            if (codeLit.Body.Count == 1)
                CompileNode(codeLit.Body[0]);
            else
                CompileNode(new SequenceNode(codeLit.Body, codeLit.Line, codeLit.Column));
        }
        else
        {
            CompileNode(body);
        }
    }

    // --- Sequence ---
    private void CompileSequence(SequenceNode node)
    {
        for (int i = 0; i < node.Expressions.Count; i++)
        {
            CompileNode(node.Expressions[i]);
            if (i < node.Expressions.Count - 1)
                _chunk.Emit(OpCode.Pop); // discard intermediate results
        }
        // Last expression value stays on stack
    }

    // --- Helpers ---
    /// <summary>
    /// Resolve a local variable slot, creating one if it doesn't exist.
    /// </summary>
    public int ResolveLocal(string name)
    {
        if (!_locals.TryGetValue(name, out int slot))
        {
            slot = _localCount++;
            _locals[name] = slot;
        }
        return slot;
    }
}

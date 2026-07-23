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

        // Reserve slot 0 for _this (function argument, set by call/spawn)
        _locals["_this"] = 0;
        _localCount = 1;

        CompileNode(ast);

        // Implicit return of last value
        _chunk.Emit(OpCode.Ret);
        _chunk.LocalCount = _localCount;
        return _chunk;
    }

    private void CompileNode(AstNode node)
    {
        _chunk.SetDebugPosition(node.Line, node.Column);
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
            case ReturnNode r: CompileReturn(r); break;
            case ForDoNode fd: CompileForDo(fd); break;
            case SwitchDoNode sw: CompileSwitchDo(sw); break;
            case ImportNode imp: CompileImport(imp); break;
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
        if (string.Equals(node.Operator, "throw", StringComparison.OrdinalIgnoreCase))
        {
            CompileNode(node.Operand);
            _chunk.Emit(OpCode.Throw);
        }
        else if (string.Equals(node.Operator, "params", StringComparison.OrdinalIgnoreCase))
        {
            CompileParams(node);
        }
        else
        {
            CompileNode(node.Operand);
            int cmdId = _chunk.AddGlobal(node.Operator);
            _chunk.Emit(OpCode.UnaryCall, cmdId);
        }
    }

    /// <summary>
    /// Compile params ["_a", ["_b", defaultValue], ...] into destructuring code.
    /// Reads from _this (local slot 0), extracts elements by index, applies defaults,
    /// and assigns to named local variables.
    /// </summary>
    private void CompileParams(UnaryCallNode node)
    {
        if (node.Operand is not ArrayExprNode arr)
        {
            // params without array — no-op, push nil
            EmitPushNil();
            return;
        }

        int selectCmdId = _chunk.AddGlobal("select");
        int isNilCmdId = _chunk.AddGlobal("isNil");

        for (int i = 0; i < arr.Elements.Count; i++)
        {
            var elem = arr.Elements[i];

            // Parse param definition: "_name" or ["_name", defaultValue]
            string varName;
            AstNode? defaultVal = null;

            if (elem is StringLiteralNode s)
            {
                varName = s.Value;
            }
            else if (elem is ArrayExprNode defArr && defArr.Elements.Count >= 1
                && defArr.Elements[0] is StringLiteralNode nameNode)
            {
                varName = nameNode.Value;
                if (defArr.Elements.Count >= 2)
                    defaultVal = defArr.Elements[1];
            }
            else
            {
                // Malformed param — skip
                continue;
            }

            // Validate variable name (must be local)
            if (!varName.StartsWith("_"))
                continue;

            int slot = ResolveLocal(varName);

            // Push _this array (local 0), push index, call select
            _chunk.Emit(OpCode.PushLocal, 0); // _this
            _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(new SqValue((double)i)));
            _chunk.Emit(OpCode.BinaryCall, selectCmdId); // _this select i

            // If default value specified, check for nil and substitute
            if (defaultVal != null)
            {
                // Stack: [... val]
                _chunk.Emit(OpCode.Dup);           // [... val val]
                _chunk.Emit(OpCode.UnaryCall, isNilCmdId); // [... val isNil(val)]
                int skipDefault = _chunk.EmitPlaceholder(OpCode.JumpIfFalse);
                // val is nil — replace with default
                _chunk.Emit(OpCode.Pop);           // [...]
                CompileNode(defaultVal);           // [... defaultVal]
                _chunk.PatchJump(skipDefault, _chunk.Count);
                // Stack: [... val] (or [... defaultVal])
            }

            // Dup for expression result (params evaluates to last value), store
            _chunk.Emit(OpCode.Dup);
            _chunk.Emit(OpCode.StoreLocal, slot);
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
        else if (TryConstantFold(node, out var folded))
        {
            // Constant folding succeeded — emit single constant
            _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(folded));
        }
        else
        {
            CompileNode(node.Left);
            CompileNode(node.Right);
            int cmdId = _chunk.AddGlobal(node.Operator);
            _chunk.Emit(OpCode.BinaryCall, cmdId);
        }
    }

    /// <summary>Try to evaluate a binary expression at compile time.</summary>
    private static bool TryConstantFold(BinaryCallNode node, out SqValue result)
    {
        result = default;
        string op = node.Operator;

        // Only fold pure arithmetic on number literals
        if (node.Left is not NumberLiteralNode ln || node.Right is not NumberLiteralNode rn)
            return false;

        double a = ln.Value;
        double b = rn.Value;

        switch (op)
        {
            case "+": result = new SqValue(a + b); return true;
            case "-": result = new SqValue(a - b); return true;
            case "*": result = new SqValue(a * b); return true;
            case "/":
                if (b == 0) return false;
                result = new SqValue(a / b); return true;
            case "%":
                if (b == 0) return false;
                result = new SqValue(a % b); return true;
            case "^": result = new SqValue(Math.Pow(a, b)); return true;
            case "==": result = new SqValue(Math.Abs(a - b) < double.Epsilon); return true;
            case "!=": result = new SqValue(Math.Abs(a - b) >= double.Epsilon); return true;
            case "<": result = new SqValue(a < b); return true;
            case ">": result = new SqValue(a > b); return true;
            case "<=": result = new SqValue(a <= b); return true;
            case ">=": result = new SqValue(a >= b); return true;
            default: return false;
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

    // --- return ---
    private void CompileReturn(ReturnNode node)
    {
        if (node.Value != null)
            CompileNode(node.Value);
        else
            EmitPushNil();
        _chunk.Emit(OpCode.Ret);
    }

    // --- for [{init},{cond},{step}] do {body} ---
    private void CompileForDo(ForDoNode node)
    {
        // Init
        CompileNode(node.Init);
        _chunk.Emit(OpCode.Pop); // discard init result

        int loopStart = _chunk.Count;

        // Condition
        CompileNode(node.Condition);
        int exitJump = _chunk.EmitPlaceholder(OpCode.JumpIfFalse);

        // Body
        _breakJumps.Push(-1);
        CompileBody(node.Body);
        _chunk.Emit(OpCode.Pop); // discard body result

        // Step
        CompileNode(node.Step);
        _chunk.Emit(OpCode.Pop); // discard step result

        // Loop back
        _chunk.Emit(OpCode.Jump, loopStart);
        _chunk.PatchJump(exitJump, _chunk.Count);

        _breakJumps.Pop();

        // for returns nil
        _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(SqValue.Nil));
    }

    // --- switch / case / default ---
    private void CompileSwitchDo(SwitchDoNode node)
    {
        // Evaluate switch value once, store in temp local
        CompileNode(node.Value);
        int valueSlot = _localCount++;
        _chunk.Emit(OpCode.StoreLocal, valueSlot);
        _chunk.Emit(OpCode.Pop); // discard assignment dup

        // Build jump table: for each case, emit condition check + conditional jump to body
        var caseJumps = new List<int>(); // jump-to-body placeholders
        var endJumps = new List<int>();  // jump-past-body placeholders (after each body)

        int defaultBodyJump = -1;

        foreach (var sc in node.Cases)
        {
            if (sc.CaseValue == null)
            {
                // default — will be placed after all cases
                continue;
            }

            // Push switch value, push case value, compare ==
            _chunk.Emit(OpCode.PushLocal, valueSlot);
            CompileNode(sc.CaseValue);
            int eqCmdId = _chunk.AddGlobal("==");
            _chunk.Emit(OpCode.BinaryCall, eqCmdId);

            // JumpIfTrue to this case's body
            int jumpToBody = _chunk.EmitPlaceholder(OpCode.JumpIfTrue);
            caseJumps.Add(jumpToBody);
        }

        // If no case matched, jump to default (or past all cases)
        int jumpToDefault = _chunk.EmitPlaceholder(OpCode.Jump);

        // Emit case bodies
        int caseIdx = 0;
        var bodyStartAddrs = new List<int>();
        foreach (var sc in node.Cases)
        {
            if (sc.CaseValue == null)
            {
                // default body — record address
                defaultBodyJump = _chunk.Count;
            }
            else
            {
                // Patch the JumpIfTrue for this case
                _chunk.PatchJump(caseJumps[caseIdx], _chunk.Count);
                caseIdx++;
            }

            bodyStartAddrs.Add(_chunk.Count);
            CompileBody(sc.Body);

            // Jump past remaining cases after executing a body
            int jumpPast = _chunk.EmitPlaceholder(OpCode.Jump);
            endJumps.Add(jumpPast);
        }

        // Patch default jump
        if (defaultBodyJump >= 0)
            _chunk.PatchJump(jumpToDefault, defaultBodyJump);
        else
            _chunk.PatchJump(jumpToDefault, _chunk.Count);

        // If no default and no case matched, push nil
        if (defaultBodyJump < 0)
            _chunk.Emit(OpCode.PushConst, _chunk.AddConstant(SqValue.Nil));

        // Patch all end jumps
        int afterSwitch = _chunk.Count;
        foreach (var j in endJumps)
            _chunk.PatchJump(j, afterSwitch);
    }

    // --- import ---
    private void CompileImport(ImportNode node)
    {
        // import "path" is a no-op at compile time.
        // The host handles file resolution and module loading.
        // Emit a nop by pushing nil.
        EmitPushNil();
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

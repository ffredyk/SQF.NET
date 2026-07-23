namespace SQSharp.Core;

/// <summary>
/// A single bytecode instruction with optional operands.
/// </summary>
public readonly struct Instruction
{
    public OpCode OpCode { get; }
    public int Operand { get; }
    public int Operand2 { get; }

    public Instruction(OpCode opCode, int operand = 0, int operand2 = 0)
    {
        OpCode = opCode;
        Operand = operand;
        Operand2 = operand2;
    }

    public override string ToString()
    {
        return OpCode switch
        {
            OpCode.PushConst => $"PUSH_CONST {Operand}",
            OpCode.PushLocal => $"PUSH_LOCAL {Operand}",
            OpCode.StoreLocal => $"STORE_LOCAL {Operand}",
            OpCode.PushGlobal => $"PUSH_GLOBAL {Operand}",
            OpCode.StoreGlobal => $"STORE_GLOBAL {Operand}",
            OpCode.NularCall => $"NULAR_CALL {Operand}",
            OpCode.UnaryCall => $"UNARY_CALL {Operand}",
            OpCode.BinaryCall => $"BINARY_CALL {Operand}",
            OpCode.MakeArray => $"MAKE_ARRAY {Operand}",
            OpCode.MakeHashMap => "MAKE_HASHMAP",
            OpCode.MakeCode => $"MAKE_CODE {Operand}",
            OpCode.Jump => $"JUMP {Operand}",
            OpCode.JumpIfFalse => $"JUMP_IF_FALSE {Operand}",
            OpCode.JumpIfTrue => $"JUMP_IF_TRUE {Operand}",
            OpCode.Call => $"CALL {Operand}",
            OpCode.Spawn => $"SPAWN {Operand}",
            OpCode.Ret => "RET",
            OpCode.Yield => "YIELD",
            OpCode.Dup => "DUP",
            OpCode.Pop => "POP",
            OpCode.Swap => "SWAP",
            OpCode.Throw => "THROW",
            OpCode.TryBegin => $"TRY_BEGIN {Operand}",
            OpCode.TryEnd => "TRY_END",
            OpCode.MakeShared => $"MAKE_SHARED",
            OpCode.IsNilLocal => $"ISNIL_LOCAL {Operand}",
            OpCode.IsNilGlobal => $"ISNIL_GLOBAL {Operand}",
            _ => $"{OpCode} {Operand}"
        };
    }
}

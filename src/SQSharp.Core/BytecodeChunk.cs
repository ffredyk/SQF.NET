using System.Collections.Generic;

namespace SQSharp.Core;

/// <summary>
/// A compiled bytecode chunk — the output of the compiler, input to the VM.
/// </summary>
public class BytecodeChunk
{
    public List<Instruction> Instructions { get; } = new();
    public List<SqValue> Constants { get; } = new();
    public List<string> GlobalNames { get; } = new();
    public List<int> CommandIds { get; } = new();
    public int LocalCount { get; set; }
    public List<BytecodeChunk> Children { get; } = new();
    public string? SourceFile { get; set; }

    public int AddConstant(SqValue value)
    {
        int idx = Constants.IndexOf(value);
        if (idx >= 0) return idx;
        Constants.Add(value);
        return Constants.Count - 1;
    }

    public int AddGlobal(string name)
    {
        int idx = GlobalNames.IndexOf(name);
        if (idx >= 0) return idx;
        GlobalNames.Add(name);
        return GlobalNames.Count - 1;
    }

    public int AddCommand(int commandId)
    {
        int idx = CommandIds.IndexOf(commandId);
        if (idx >= 0) return idx;
        CommandIds.Add(commandId);
        return CommandIds.Count - 1;
    }

    public int Count => Instructions.Count;

    public void Emit(OpCode op, int operand = 0, int operand2 = 0)
    {
        Instructions.Add(new Instruction(op, operand, operand2));
    }

    public int EmitPlaceholder(OpCode op)
    {
        Instructions.Add(new Instruction(op, -1));
        return Instructions.Count - 1;
    }

    public void PatchJump(int placeholderIndex, int targetIndex)
    {
        var inst = Instructions[placeholderIndex];
        Instructions[placeholderIndex] = new Instruction(inst.OpCode, targetIndex);
    }
}

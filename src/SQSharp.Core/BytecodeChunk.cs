using System.Collections.Generic;

namespace SQSharp.Core;

/// <summary>
/// Source position for debug info.
/// </summary>
public readonly struct DebugInfoEntry
{
    public int InstructionIndex { get; }
    public int Line { get; }
    public int Column { get; }

    public DebugInfoEntry(int instructionIndex, int line, int column)
    {
        InstructionIndex = instructionIndex;
        Line = line;
        Column = column;
    }
}

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

    /// <summary>Source position debug info — maps instruction index to source line/column.</summary>
    public List<DebugInfoEntry> DebugInfo { get; } = new();

    /// <summary>Current source position for the next emitted instruction.</summary>
    private int _currentDebugLine;
    private int _currentDebugColumn;

    /// <summary>Set the source position for subsequent Emit calls.</summary>
    public void SetDebugPosition(int line, int column)
    {
        _currentDebugLine = line;
        _currentDebugColumn = column;
    }

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
        DebugInfo.Add(new DebugInfoEntry(Instructions.Count - 1, _currentDebugLine, _currentDebugColumn));
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

    /// <summary>Record source position for the last emitted instruction.</summary>
    public void RecordDebugInfo(int line, int column)
    {
        if (Instructions.Count > 0)
            DebugInfo.Add(new DebugInfoEntry(Instructions.Count - 1, line, column));
    }

    /// <summary>Get source position for an instruction index (binary search).</summary>
    public (int line, int col) GetDebugInfo(int instructionIndex)
    {
        int line = 0, col = 0;
        // Linear scan backwards to find closest debug entry
        for (int i = DebugInfo.Count - 1; i >= 0; i--)
        {
            if (DebugInfo[i].InstructionIndex <= instructionIndex)
            {
                line = DebugInfo[i].Line;
                col = DebugInfo[i].Column;
                break;
            }
        }
        return (line, col);
    }
}

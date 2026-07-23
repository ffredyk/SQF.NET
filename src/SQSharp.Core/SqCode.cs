namespace SQSharp.Core;

/// <summary>
/// Represents a compiled code block — a first-class value in SQ#.
/// Code blocks are created by the compiler from { ... } syntax,
/// or at runtime via the compile command.
/// </summary>
public sealed class SqCode
{
    /// <summary>Optional source text (for debugging/display).</summary>
    public string? SourceText { get; }

    /// <summary>Number of expected arguments (params count). 0 for no params.</summary>
    public int ParameterCount { get; }

    /// <summary>Compiled bytecode for this code block.</summary>
    internal byte[]? Bytecode { get; }

    /// <summary>Offset into the parent chunk's children where the bytecode lives.</summary>
    public int BytecodeOffset { get; }

    /// <summary>For runtime-compiled code: the standalone compiled chunk.</summary>
    public BytecodeChunk? CompiledChunk { get; }

    /// <summary>Whether this code was compiled as final (immutable).</summary>
    public bool IsFinal { get; }

    public SqCode(string? sourceText, int parameterCount = 0,
        byte[]? bytecode = null, int bytecodeOffset = 0, bool isFinal = false,
        BytecodeChunk? compiledChunk = null)
    {
        SourceText = sourceText;
        ParameterCount = parameterCount;
        Bytecode = bytecode;
        BytecodeOffset = bytecodeOffset;
        IsFinal = isFinal;
        CompiledChunk = compiledChunk;
    }

    public override string ToString() => SourceText ?? "{code}";
}

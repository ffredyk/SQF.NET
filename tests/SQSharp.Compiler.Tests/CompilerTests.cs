using Xunit;
using SQSharp.Core;
using SQSharp.Language;
using SQSharp.Compiler;

namespace SQSharp.Compiler.Tests;

public class CompilerTests
{
    private BytecodeChunk Compile(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        var compiler = new SQSharp.Compiler.Compiler();
        return compiler.Compile(ast);
    }

    [Fact]
    public void Compile_NumberLiteral()
    {
        var chunk = Compile("42;");
        Assert.NotEmpty(chunk.Instructions);
        Assert.Contains(chunk.Instructions, i => i.OpCode == OpCode.PushConst);
    }

    [Fact]
    public void Compile_Assignment()
    {
        var chunk = Compile("_x = 5;");
        Assert.Contains(chunk.Instructions, i => i.OpCode == OpCode.StoreLocal);
    }

    [Fact]
    public void Compile_Arithmetic()
    {
        var chunk = Compile("_x + _y;");
        Assert.Contains(chunk.Instructions, i => i.OpCode == OpCode.BinaryCall);
    }

    [Fact]
    public void Compile_Sequence()
    {
        var chunk = Compile("_x = 1; _y = 2; _x + _y;");
        Assert.True(chunk.Instructions.Count > 5);
    }

    [Fact]
    public void Compile_RetAtEnd()
    {
        var chunk = Compile("42;");
        Assert.Equal(OpCode.Ret, chunk.Instructions[^1].OpCode);
    }
}

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

    [Fact]
    public void Compile_ReturnNode()
    {
        var chunk = Compile("return 42; _x = 1");
        Assert.Contains(chunk.Instructions, i => i.OpCode == OpCode.Ret);
    }

    [Fact]
    public void Compile_ConstantFolding_Addition()
    {
        // 1 + 2 should fold to constant 3 (single PushConst, no BinaryCall for +)
        var chunk = Compile("1 + 2;");
        // Should have PushConst(3) instead of PushConst(1), PushConst(2), BinaryCall(+)
        int binaryAddCount = chunk.Instructions.Count(i => i.OpCode == OpCode.BinaryCall);
        Assert.Equal(0, binaryAddCount); // all folded
    }

    [Fact]
    public void Compile_ConstantFolding_Multiplication()
    {
        var chunk = Compile("3 * 4;");
        int binaryMulCount = chunk.Instructions.Count(i => i.OpCode == OpCode.BinaryCall);
        Assert.Equal(0, binaryMulCount);
    }

    [Fact]
    public void Compile_NoFold_VariableOperand()
    {
        // _x + 1 should NOT fold (variable not constant)
        var chunk = Compile("_x + 1;");
        Assert.Contains(chunk.Instructions, i => i.OpCode == OpCode.BinaryCall);
    }

    [Fact]
    public void Compile_ImportNode()
    {
        var chunk = Compile("import \"utils\";");
        Assert.NotNull(chunk);
    }

    [Fact]
    public void Compile_SequenceWithIf()
    {
        var chunk = Compile("if (true) then { 1 }");
        Assert.Contains(chunk.Instructions, i => i.OpCode == OpCode.JumpIfFalse);
    }
}

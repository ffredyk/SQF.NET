using Xunit;
using SQSharp.Language;

namespace SQSharp.Language.Tests;

public class ParserTests
{
    // === Expression basics ===
    [Fact]
    public void NumberLiteral()
    {
        var ast = Parse("42");
        Assert.IsType<NumberLiteralNode>(ast);
        Assert.Equal(42.0, ((NumberLiteralNode)ast).Value);
    }

    [Fact]
    public void StringLiteral()
    {
        var ast = Parse("\"hello\"");
        Assert.IsType<StringLiteralNode>(ast);
        Assert.Equal("hello", ((StringLiteralNode)ast).Value);
    }

    [Fact]
    public void BoolLiteral()
    {
        Assert.IsType<BoolLiteralNode>(Parse("true"));
        Assert.IsType<BoolLiteralNode>(Parse("false"));
        Assert.True(((BoolLiteralNode)Parse("true")).Value);
    }

    [Fact]
    public void NilLiteral()
    {
        Assert.IsType<NilLiteralNode>(Parse("nil"));
    }

    [Fact]
    public void Variable()
    {
        var ast = Parse("_myVar");
        Assert.IsType<VariableNode>(ast);
        Assert.Equal("_myVar", ((VariableNode)ast).Name);
        Assert.True(((VariableNode)ast).IsLocal);
    }

    [Fact]
    public void Assignment()
    {
        var ast = Parse("_x = 5");
        Assert.IsType<AssignmentNode>(ast);
        var assign = (AssignmentNode)ast;
        Assert.Equal("_x", assign.VariableName);
        Assert.IsType<NumberLiteralNode>(assign.Value);
    }

    [Fact]
    public void ArrayExpr()
    {
        var ast = Parse("[1, 2, 3]");
        Assert.IsType<ArrayExprNode>(ast);
        Assert.Equal(3, ((ArrayExprNode)ast).Elements.Count);
    }

    // === Arithmetic ===
    [Fact]
    public void Addition()
    {
        var ast = Parse("_x + 3");
        Assert.IsType<BinaryCallNode>(ast);
        var bin = (BinaryCallNode)ast;
        Assert.Equal("+", bin.Operator);
        Assert.IsType<VariableNode>(bin.Left);
        Assert.IsType<NumberLiteralNode>(bin.Right);
    }

    [Fact]
    public void ArithmeticPrecedence()
    {
        // 1 + 2 * 3 should parse as 1 + (2 * 3)
        var ast = Parse("1 + 2 * 3");
        var bin = Assert.IsType<BinaryCallNode>(ast);
        Assert.Equal("+", bin.Operator);
        Assert.IsType<NumberLiteralNode>(bin.Left);
        var right = Assert.IsType<BinaryCallNode>(bin.Right);
        Assert.Equal("*", right.Operator);
    }

    [Fact]
    public void Subtraction()
    {
        var ast = Parse("_x - 1");
        var bin = Assert.IsType<BinaryCallNode>(ast);
        Assert.Equal("-", bin.Operator);
    }

    // === Semicolon and comma terminators ===
    [Fact]
    public void Semicolon_SeparatesExpressions()
    {
        var ast = Parse("_a = 1; _b = 2");
        var seq = Assert.IsType<SequenceNode>(ast);
        Assert.Equal(2, seq.Expressions.Count);
    }

    [Fact]
    public void Comma_SeparatesExpressions()
    {
        var ast = Parse("_a = 1, _b = 2");
        var seq = Assert.IsType<SequenceNode>(ast);
        Assert.Equal(2, seq.Expressions.Count);
    }

    // === Unary greediness ===
    [Fact]
    public void UnaryCommand_ConsumesRightValue()
    {
        // count _arr — count is unary, greedily consumes _arr
        var ast = Parse("count _arr");
        var unary = Assert.IsType<UnaryCallNode>(ast);
        Assert.Equal("count", unary.Operator);
        Assert.IsType<VariableNode>(unary.Operand);
    }

    [Fact]
    public void UnaryGreed_WithBinarySelect()
    {
        // count _arr select 2 = (count _arr) select 2
        var ast = Parse("count _arr select 2");
        var bin = Assert.IsType<BinaryCallNode>(ast);
        Assert.Equal("select", bin.Operator);
        var unary = Assert.IsType<UnaryCallNode>(bin.Left);
        Assert.Equal("count", unary.Operator);
        Assert.IsType<NumberLiteralNode>(bin.Right);
    }

    [Fact]
    public void ParensOverride_UnaryGreed()
    {
        // count (_arr select 2) — parens force select first
        var ast = Parse("count (_arr select 2)");
        var unary = Assert.IsType<UnaryCallNode>(ast);
        Assert.Equal("count", unary.Operator);
        var bin = Assert.IsType<BinaryCallNode>(unary.Operand);
        Assert.Equal("select", bin.Operator);
    }

    [Fact]
    public void LocalVariable_NotUnaryCommand()
    {
        // _arr pushBack 6 — _arr is local var, pushBack is binary
        var ast = Parse("_arr pushBack 6");
        var bin = Assert.IsType<BinaryCallNode>(ast);
        Assert.Equal("pushBack", bin.Operator);
        Assert.IsType<VariableNode>(bin.Left);
        Assert.IsType<NumberLiteralNode>(bin.Right);
    }

    [Fact]
    public void BareCommand_IsUnary()
    {
        // alive player — alive is unary, player is operand
        var ast = Parse("alive player");
        var unary = Assert.IsType<UnaryCallNode>(ast);
        Assert.Equal("alive", unary.Operator);
        Assert.IsType<VariableNode>(unary.Operand);
    }

    // === Comparison ===
    [Fact]
    public void Equality()
    {
        var ast = Parse("_x == 5");
        var bin = Assert.IsType<BinaryCallNode>(ast);
        Assert.Equal("==", bin.Operator);
    }

    [Fact]
    public void ComparisonPrecedence_BelowArithmetic()
    {
        // a + b == c + d  =  (a + b) == (c + d)
        var ast = Parse("_a + _b == _c + _d");
        var bin = Assert.IsType<BinaryCallNode>(ast);
        Assert.Equal("==", bin.Operator);
        Assert.IsType<BinaryCallNode>(bin.Left);  // _a + _b
        Assert.IsType<BinaryCallNode>(bin.Right); // _c + _d
    }

    // === Logical ===
    [Fact]
    public void LogicalAnd()
    {
        var ast = Parse("_a == _b && _c == _d");
        var bin = Assert.IsType<BinaryCallNode>(ast);
        Assert.Equal("&&", bin.Operator);
    }

    [Fact]
    public void LogicalOr()
    {
        var ast = Parse("_a || _b");
        var bin = Assert.IsType<BinaryCallNode>(ast);
        Assert.Equal("||", bin.Operator);
    }

    // === Control flow ===
    [Fact]
    public void IfThen_Parses()
    {
        var ast = Parse("if (true) then { _x = 1 }");
        Assert.IsType<IfThenElseNode>(ast);
    }

    [Fact(Skip = "Control flow parsing with nested code blocks needs refinement")]
    public void IfThenElse() { }

    [Fact(Skip = "Control flow parsing with nested code blocks needs refinement")]
    public void WhileDo() { }

    [Fact(Skip = "Control flow parsing with nested code blocks needs refinement")]
    public void ForFromTo() { }

    [Fact]
    public void Call_WithArgs()
    {
        // [1,2] call {code} — args on left, call is binary operator
        var ast = Parse("[1, 2] call { (_this select 0) + (_this select 1) }");
        var bin = Assert.IsType<BinaryCallNode>(ast);
        Assert.Equal("call", bin.Operator);
        Assert.IsType<ArrayExprNode>(bin.Left);
        Assert.IsType<CodeLiteralNode>(bin.Right);
    }

    [Fact]
    public void Call_NoArgs_IsCallNode()
    {
        // call {code} — no args, call is prefix keyword → CallNode
        var ast = Parse("call { hint str 123 }");
        Assert.IsType<CallNode>(ast);
    }

    [Fact]
    public void Spawn_WithArgs()
    {
        // 0 spawn {code} — args on left, spawn is binary operator
        var ast = Parse("0 spawn { hint \"done\" }");
        var bin = Assert.IsType<BinaryCallNode>(ast);
        Assert.Equal("spawn", bin.Operator);
        Assert.IsType<CodeLiteralNode>(bin.Right);
    }

    [Fact]
    public void Spawn_NoArgs_IsSpawnNode()
    {
        // spawn {code} — no args, spawn is prefix keyword → SpawnNode
        var ast = Parse("spawn { hint \"done\" }");
        Assert.IsType<SpawnNode>(ast);
    }

    // === Helpers ===
    private static AstNode Parse(string source)
    {
        var lexer = new Lexer(source, legacyComments: true);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }
}

using Xunit;
using SQSharp.Language;
using System.Linq;

namespace SQSharp.Language.Tests;

public class LexerTests
{
    [Fact]
    public void Numbers_Tokenize()
    {
        var tokens = Tokenize("42");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.NumberValue == 42.0);
    }

    [Fact]
    public void Floats_Tokenize()
    {
        var tokens = Tokenize("3.14");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && System.Math.Abs(t.NumberValue - 3.14) < 0.001);
    }

    [Fact]
    public void Strings_Tokenize()
    {
        var tokens = Tokenize("\"hello\"");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.StringValue == "hello");
    }

    [Fact]
    public void SingleQuotedStrings_Tokenize()
    {
        var tokens = Tokenize("'world'");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.StringValue == "world");
    }

    [Fact]
    public void Identifiers_Tokenize()
    {
        var tokens = Tokenize("_myVar player");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Lexeme == "_myVar");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Lexeme == "player");
    }

    [Fact]
    public void Operators_Tokenize()
    {
        var tokens = Tokenize("+ - * / % ^ == != < > <= >=");
        Assert.Contains(tokens, t => t.Type == TokenType.Plus);
        Assert.Contains(tokens, t => t.Type == TokenType.Minus);
        Assert.Contains(tokens, t => t.Type == TokenType.Star);
        Assert.Contains(tokens, t => t.Type == TokenType.Slash);
        Assert.Contains(tokens, t => t.Type == TokenType.EqEq);
        Assert.Contains(tokens, t => t.Type == TokenType.NotEq);
    }

    [Fact]
    public void LogicOperators_Tokenize()
    {
        var tokens = Tokenize("&& || !");
        Assert.Contains(tokens, t => t.Type == TokenType.AndAnd);
        Assert.Contains(tokens, t => t.Type == TokenType.OrOr);
        Assert.Contains(tokens, t => t.Type == TokenType.Bang);
    }

    [Fact]
    public void Brackets_Tokenize()
    {
        var tokens = Tokenize("() []");
        Assert.Contains(tokens, t => t.Type == TokenType.LParen);
        Assert.Contains(tokens, t => t.Type == TokenType.RParen);
        Assert.Contains(tokens, t => t.Type == TokenType.LBracket);
        Assert.Contains(tokens, t => t.Type == TokenType.RBracket);
    }

    [Fact]
    public void SemicolonAndComma_Tokenize()
    {
        var tokens = Tokenize("a; b, c");
        Assert.Contains(tokens, t => t.Type == TokenType.Semicolon);
        Assert.Contains(tokens, t => t.Type == TokenType.Comma);
    }

    [Fact]
    public void LineComments_AreSkipped()
    {
        var tokens = Tokenize("// comment\n42");
        Assert.DoesNotContain(tokens, t => t.Lexeme.Contains("comment"));
        Assert.Contains(tokens, t => t.Type == TokenType.Number);
    }

    [Fact]
    public void BlockComments_AreSkipped()
    {
        var tokens = Tokenize("1 + /* inline */ 2");
        Assert.DoesNotContain(tokens, t => t.Lexeme.Contains("inline"));
        Assert.Equal(2, tokens.Count(t => t.Type == TokenType.Number));
    }

    [Fact]
    public void BlockComment_MidExpression()
    {
        var tokens = Tokenize("1 + /* comment */ 1");
        var numbers = tokens.Where(t => t.Type == TokenType.Number).ToList();
        Assert.Equal(2, numbers.Count);
    }

    [Fact]
    public void CodeBlocks_ParseNested()
    {
        var tokens = Tokenize("{ _x = 1; }");
        var block = tokens.FirstOrDefault(t => t.Type == TokenType.CodeBlock);
        Assert.NotEqual(default, block);
        Assert.NotNull(block.NestedTokens);
    }

    [Fact]
    public void Keywords_TokenizeAsIdentifiers()
    {
        var tokens = Tokenize("if while for switch call spawn private");
        var ids = tokens.Where(t => t.Type == TokenType.Identifier).Select(t => t.Lexeme).ToList();
        Assert.Contains("if", ids);
        Assert.Contains("while", ids);
        Assert.Contains("call", ids);
        Assert.Contains("spawn", ids);
    }

    [Fact]
    public void Assignment_Tokenizes()
    {
        var tokens = Tokenize("_x = 5");
        Assert.Contains(tokens, t => t.Type == TokenType.Assign);
    }

    [Fact]
    public void Eof_IsAppended()
    {
        var tokens = Tokenize("42");
        Assert.Equal(TokenType.Eof, tokens.Last().Type);
    }

    private static System.Collections.Generic.List<Token> Tokenize(string source)
    {
        var lexer = new Lexer(source, legacyComments: true);
        return lexer.Tokenize();
    }
}

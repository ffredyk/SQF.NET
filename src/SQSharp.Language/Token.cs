namespace SQSharp.Language;

/// <summary>
/// All token types in the SQ# language.
/// </summary>
public enum TokenType
{
    // Values
    Identifier,       // bare word or _variable
    Number,           // 123, 45.67, 1e10
    String,           // "hello" or 'hello'
    CodeBlock,        // { ... } — parsed as nested token stream

    // Arithmetic
    Plus,             // +
    Minus,            // -
    Star,             // *
    Slash,            // /
    Percent,          // %
    Caret,            // ^

    // Comparison
    EqEq,             // ==
    NotEq,            // !=
    Less,             // <
    Greater,          // >
    LessEq,           // <=
    GreaterEq,        // >=

    // Logical
    AndAnd,           // &&
    OrOr,             // ||
    Bang,             // !

    // Brackets
    LParen,           // (
    RParen,           // )
    LBracket,         // [
    RBracket,         // ]

    // Other
    Hash,             // #
    Colon,            // :
    Semicolon,        // ;
    Comma,            // ,
    Assign,           // =
    Dot,              // .

    // Special
    Eof,              // End of file
    Error,            // Lexer error
}

/// <summary>
/// A single token produced by the lexer.
/// </summary>
public readonly struct Token
{
    public TokenType Type { get; }
    public string Lexeme { get; }
    public int Line { get; }
    public int Column { get; }

    /// <summary>For Number tokens: the parsed numeric value.</summary>
    public double NumberValue { get; }

    /// <summary>For String tokens: the unescaped string value.</summary>
    public string? StringValue { get; }

    /// <summary>For CodeBlock tokens: nested token stream (lazy parsed).</summary>
    public List<Token>? NestedTokens { get; }

    public Token(TokenType type, string lexeme, int line, int column,
        double numberValue = 0, string? stringValue = null,
        List<Token>? nestedTokens = null)
    {
        Type = type;
        Lexeme = lexeme;
        Line = line;
        Column = column;
        NumberValue = numberValue;
        StringValue = stringValue;
        NestedTokens = nestedTokens;
    }

    public override string ToString() =>
        Type switch
        {
            TokenType.Number => $"NUM({NumberValue})",
            TokenType.String => $"STR(\"{StringValue}\")",
            TokenType.Identifier => $"ID({Lexeme})",
            TokenType.Eof => "EOF",
            _ => $"'{Lexeme}'"
        };
}

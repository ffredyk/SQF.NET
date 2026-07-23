using System;
using System.Collections.Generic;
using System.Globalization;

namespace SQSharp.Language;

/// <summary>
/// Lexer for SQ# source code. Converts raw text into a stream of tokens.
/// Handles SQF-style strings (double and single quoted), comments, numbers, and operators.
/// </summary>
public class Lexer
{
    private readonly string _source;
    private int _pos;
    private int _line = 1;
    private int _col = 1;
    private readonly List<Token> _tokens = new();
    private bool _legacyComments; // Strip comments (preprocessor phase)

    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["true"] = TokenType.Identifier,   // treated as identifier, resolved at parse time
        ["false"] = TokenType.Identifier,
        ["nil"] = TokenType.Identifier,
        ["not"] = TokenType.Identifier,
        ["and"] = TokenType.Identifier,
        ["or"] = TokenType.Identifier,
        ["mod"] = TokenType.Identifier,
        ["private"] = TokenType.Identifier,
        ["global"] = TokenType.Identifier,
        ["if"] = TokenType.Identifier,
        ["then"] = TokenType.Identifier,
        ["else"] = TokenType.Identifier,
        ["while"] = TokenType.Identifier,
        ["do"] = TokenType.Identifier,
        ["for"] = TokenType.Identifier,
        ["from"] = TokenType.Identifier,
        ["to"] = TokenType.Identifier,
        ["step"] = TokenType.Identifier,
        ["switch"] = TokenType.Identifier,
        ["case"] = TokenType.Identifier,
        ["default"] = TokenType.Identifier,
        ["call"] = TokenType.Identifier,
        ["spawn"] = TokenType.Identifier,
        ["execVM"] = TokenType.Identifier,
        ["import"] = TokenType.Identifier,
        ["return"] = TokenType.Identifier,
        ["try"] = TokenType.Identifier,
        ["catch"] = TokenType.Identifier,
        ["throw"] = TokenType.Identifier,
        ["with"] = TokenType.Identifier,
    };

    public Lexer(string source, bool legacyComments = true)
    {
        _source = source;
        _legacyComments = legacyComments;
    }

    public List<Token> Tokenize()
    {
        _tokens.Clear();
        _pos = 0;
        _line = 1;
        _col = 1;

        while (_pos < _source.Length)
        {
            char c = Peek();

            // Whitespace
            if (c == ' ' || c == '\t')
            {
                Advance();
                continue;
            }
            if (c == '\r' || c == '\n')
            {
                AdvanceLine();
                continue;
            }

            // Comments (preprocessor phase — strip)
            if (_legacyComments && c == '/' && Peek(1) == '/')
            {
                SkipLineComment();
                continue;
            }
            if (_legacyComments && c == '/' && Peek(1) == '*')
            {
                SkipBlockComment();
                continue;
            }

            // Numbers
            if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1))))
            {
                TokenizeNumber();
                continue;
            }

            // Strings
            if (c == '"' || c == '\'')
            {
                TokenizeString(c);
                continue;
            }

            // Identifier or keyword
            if (char.IsLetter(c) || c == '_')
            {
                TokenizeIdentifier();
                continue;
            }

            // Operators and punctuation
            TokenizeOperator();
        }

        _tokens.Add(new Token(TokenType.Eof, "", _line, _col));
        return _tokens;
    }

    private void TokenizeNumber()
    {
        int start = _pos;
        int startCol = _col;
        bool hasDot = false;
        bool hasExp = false;

        while (_pos < _source.Length)
        {
            char c = Peek();
            if (char.IsDigit(c))
            {
                Advance();
            }
            else if (c == '.' && !hasDot && !hasExp)
            {
                hasDot = true;
                Advance();
            }
            else if ((c == 'e' || c == 'E') && !hasExp)
            {
                hasExp = true;
                Advance();
                if (_pos < _source.Length && (Peek() == '+' || Peek() == '-'))
                    Advance();
            }
            else
            {
                break;
            }
        }

        string lexeme = _source[start.._pos];
        double value = double.Parse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture);
        _tokens.Add(new Token(TokenType.Number, lexeme, _line, startCol, numberValue: value));
    }

    private void TokenizeString(char quote)
    {
        int startCol = _col;
        Advance(); // opening quote
        var sb = new System.Text.StringBuilder();
        bool escaped = false;

        while (_pos < _source.Length)
        {
            char c = Peek();
            Advance();

            if (escaped)
            {
                sb.Append(c switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    '"' => '"',
                    '\'' => '\'',
                    '0' => '\0',
                    _ => c
                });
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else if (c == quote)
            {
                // Check for SQF-style double-quote escaping: "say ""hello"""
                if (quote == '"' && Peek(0) == '"')
                {
                    sb.Append('"');
                    Advance();
                }
                else
                {
                    break; // end of string
                }
            }
            else if (c == '\n' || c == '\r')
            {
                // Multi-line string — include the newline
                sb.Append(c);
                if (c == '\r' && Peek(0) == '\n') { sb.Append('\n'); Advance(); }
                _line++;
                _col = 1;
            }
            else
            {
                sb.Append(c);
            }
        }

        int lexStart = startCol > 0 ? startCol - 1 : 0;
        int lexEnd = Math.Min(_pos, _source.Length);
        string lexeme = lexStart < lexEnd ? _source[lexStart..lexEnd] : "";
        _tokens.Add(new Token(TokenType.String, lexeme, _line, startCol, stringValue: sb.ToString()));
    }

    private void TokenizeIdentifier()
    {
        int start = _pos;
        int startCol = _col;
        while (_pos < _source.Length && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
            Advance();

        string lexeme = _source[start.._pos];
        _tokens.Add(new Token(TokenType.Identifier, lexeme, _line, startCol));
    }

    private void TokenizeOperator()
    {
        char c = Peek();
        int startCol = _col;

        switch (c)
        {
            case '+': AddToken(TokenType.Plus, "+", startCol); Advance(); break;
            case '-': AddToken(TokenType.Minus, "-", startCol); Advance(); break;
            case '*': AddToken(TokenType.Star, "*", startCol); Advance(); break;
            case '%': AddToken(TokenType.Percent, "%", startCol); Advance(); break;
            case '^': AddToken(TokenType.Caret, "^", startCol); Advance(); break;
            case '#': AddToken(TokenType.Hash, "#", startCol); Advance(); break;
            case ':': AddToken(TokenType.Colon, ":", startCol); Advance(); break;
            case ';': AddToken(TokenType.Semicolon, ";", startCol); Advance(); break;
            case ',': AddToken(TokenType.Comma, ",", startCol); Advance(); break;
            case '.': AddToken(TokenType.Dot, ".", startCol); Advance(); break;
            case '(': AddToken(TokenType.LParen, "(", startCol); Advance(); break;
            case ')': AddToken(TokenType.RParen, ")", startCol); Advance(); break;
            case '[': AddToken(TokenType.LBracket, "[", startCol); Advance(); break;
            case ']': AddToken(TokenType.RBracket, "]", startCol); Advance(); break;

            case '/':
                Advance();
                if (_pos < _source.Length && Peek() == '=')
                {
                    Advance();
                    AddToken(TokenType.Slash, "/=", startCol); // future: /= operator
                }
                else
                {
                    AddToken(TokenType.Slash, "/", startCol);
                }
                break;

            case '=':
                Advance();
                if (_pos < _source.Length && Peek() == '=')
                {
                    Advance();
                    AddToken(TokenType.EqEq, "==", startCol);
                }
                else
                {
                    AddToken(TokenType.Assign, "=", startCol);
                }
                break;

            case '!':
                Advance();
                if (_pos < _source.Length && Peek() == '=')
                {
                    Advance();
                    AddToken(TokenType.NotEq, "!=", startCol);
                }
                else
                {
                    AddToken(TokenType.Bang, "!", startCol);
                }
                break;

            case '<':
                Advance();
                if (_pos < _source.Length && Peek() == '=')
                {
                    Advance();
                    AddToken(TokenType.LessEq, "<=", startCol);
                }
                else
                {
                    AddToken(TokenType.Less, "<", startCol);
                }
                break;

            case '>':
                Advance();
                if (_pos < _source.Length && Peek() == '=')
                {
                    Advance();
                    AddToken(TokenType.GreaterEq, ">=", startCol);
                }
                else
                {
                    AddToken(TokenType.Greater, ">", startCol);
                }
                break;

            case '&':
                Advance();
                if (_pos < _source.Length && Peek() == '&')
                {
                    Advance();
                    AddToken(TokenType.AndAnd, "&&", startCol);
                }
                else
                {
                    AddToken(TokenType.Error, "&", startCol); // single & not valid
                }
                break;

            case '|':
                Advance();
                if (_pos < _source.Length && Peek() == '|')
                {
                    Advance();
                    AddToken(TokenType.OrOr, "||", startCol);
                }
                else
                {
                    AddToken(TokenType.Error, "|", startCol); // single | not valid
                }
                break;

            case '{':
                TokenizeCodeBlock();
                break;

            default:
                AddToken(TokenType.Error, c.ToString(), startCol);
                Advance();
                break;
        }
    }

    private void TokenizeCodeBlock()
    {
        int startCol = _col;
        Advance(); // opening {
        int braceDepth = 1;
        int blockStart = _pos;
        int blockLine = _line;
        int blockCol = _col;

        while (_pos < _source.Length && braceDepth > 0)
        {
            char c = Peek();
            if (c == '{') { braceDepth++; Advance(); }
            else if (c == '}') { braceDepth--; if (braceDepth > 0) Advance(); }
            else if (c == '"' || c == '\'') { SkipString(c); }
            else if (c == '/' && Peek(1) == '/') { SkipLineComment(); }
            else if (c == '/' && Peek(1) == '*') { SkipBlockComment(); }
            else Advance();
        }

        if (_pos < _source.Length && Peek() == '}')
            Advance(); // closing }

        string innerSource = _source[blockStart..(_pos - 1)];
        // Recursively lex the inner code block
        var innerLexer = new Lexer(innerSource, _legacyComments);
        var nestedTokens = innerLexer.Tokenize();

        _tokens.Add(new Token(TokenType.CodeBlock, "{...}", blockLine, blockCol, nestedTokens: nestedTokens));
    }

    private void SkipLineComment()
    {
        while (_pos < _source.Length && Peek() != '\n' && Peek() != '\r')
            Advance();
    }

    private void SkipBlockComment()
    {
        Advance(); // /
        Advance(); // *
        while (_pos < _source.Length)
        {
            if (Peek() == '*' && Peek(1) == '/')
            {
                Advance(); Advance();
                return;
            }
            if (Peek() == '\n') { _line++; _col = 1; }
            Advance();
        }
    }

    private void SkipString(char quote)
    {
        Advance(); // opening quote
        while (_pos < _source.Length)
        {
            char c = Peek();
            Advance();
            if (c == '\\') { Advance(); continue; } // skip escaped char
            if (c == quote)
            {
                if (quote == '"' && Peek(0) == '"') Advance(); // SQF double-double
                else break;
            }
        }
    }

    // --- Helpers ---
    private char Peek(int offset = 0) =>
        _pos + offset < _source.Length ? _source[_pos + offset] : '\0';

    private void Advance()
    {
        if (_pos < _source.Length)
        {
            _col++;
            _pos++;
        }
    }

    private void AdvanceLine()
    {
        if (_pos < _source.Length && Peek() == '\r') _pos++;
        if (_pos < _source.Length && Peek() == '\n') _pos++;
        _line++;
        _col = 1;
    }

    private void AddToken(TokenType type, string lexeme, int col)
    {
        _tokens.Add(new Token(type, lexeme, _line, col));
    }
}

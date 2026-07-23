using System;
using System.Collections.Generic;
using SQSharp.Core;

namespace SQSharp.Language;

/// <summary>
/// Pratt (precedence-climbing) parser for SQ#.
/// Converts token stream into AST, handling operator precedence and control structure desugaring.
///
/// SQF Parameter Arity Rule (STRICT):
/// Every command takes exactly ONE expression on right side (unary)
/// or ONE on left + ONE on right (binary).
/// Commands needing 3+ parameters MUST use an ARRAY on the right side.
/// Correct: count _arr, _arr set [idx,val], spawnOn ["AI",{code}]
/// Wrong: spawnOn "AI" {code} — two right-side arguments.
/// </summary>
public class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;
    private Token _previous; // last consumed token (for error reporting)

    // Precedence levels (matching SQF: higher = tighter binding)
    public const int PrecNone = 0;
    public const int PrecOr = 1;         // ||, or
    public const int PrecAnd = 2;        // &&, and
    public const int PrecComparison = 3; // ==, !=, <, >, <=, >=
    public const int PrecBinary = 4;     // select, set, resize, switch colon :
    public const int PrecElse = 5;       // else
    public const int PrecAddSub = 6;     // +, -, min, max
    public const int PrecMulDiv = 7;     // *, /, %, mod, atan2
    public const int PrecPower = 8;      // ^
    public const int PrecCall = 9;       // call, spawn
    public const int PrecHash = 10;      // #
    public const int PrecUnary = 11;     // prefix +, -, !, not, unary commands
    public const int PrecNular = 11;     // variables, literals, brackets

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    public AstNode Parse()
    {
        var expressions = new List<AstNode>();
        while (!IsAtEnd())
        {
            var expr = ParseExpression();
            expressions.Add(expr);
            // Semicolons are optional between expressions in SQ#
            // (control structures like if/while already consume their own trailing ;)
            Match(TokenType.Semicolon);
            Match(TokenType.Comma);
        }
        return expressions.Count == 1
            ? expressions[0]
            : new SequenceNode(expressions, 1, 1);
    }

    // --- Expression parsing ---
    private AstNode ParseExpression(int precedence = PrecNone)
    {
        var left = ParsePrefix();

        // SQF unary greediness: if prefix is a potential command name (NOT a _local),
        // and next token starts an atomic expression, treat prefix as unary command.
        if (left is VariableNode varNode && !varNode.IsLocal
            && !IsAtEnd() && CanStartAtomicExpression(Peek().Type))
        {
            // Don't greed if next token is an expression breaker (like 'do', 'then', etc.)
            if (Peek().Type != TokenType.Identifier || !IsExpressionBreaker(Peek().Lexeme))
            {
                var operand = ParseExpression(PrecUnary);
                left = new UnaryCallNode(varNode.Name, operand, varNode.Line, varNode.Column);
            }
        }

        // Infix loop for binary operators
        while (true)
        {
            int nextPrec = GetEffectivePrecedence(Peek());
            if (precedence >= nextPrec) break;
            left = ParseInfix(left, nextPrec);
        }

        return left;
    }

    // --- Prefix parsing ---
    private AstNode ParsePrefix()
    {
        Token token = Advance();

        switch (token.Type)
        {
            // Literals
            case TokenType.Number:
                return new NumberLiteralNode(token.NumberValue, token.Line, token.Column);

            case TokenType.String:
                return new StringLiteralNode(token.StringValue!, token.Line, token.Column);

            case TokenType.CodeBlock:
                var bodyParser = new Parser(token.NestedTokens!);
                var body = bodyParser.Parse();
                var bodyList = body is SequenceNode seq ? seq.Expressions : new List<AstNode> { body };
                return new CodeLiteralNode(bodyList, token.Line, token.Column);

            // Bracketed expressions
            case TokenType.LParen:
                var expr = ParseExpression();
                Expect(TokenType.RParen, "Expected ')'");
                return expr;

            case TokenType.LBracket:
                return ParseArray();

            // Unary operators
            case TokenType.Plus:
            case TokenType.Minus:
            case TokenType.Bang:
                {
                    var operand = ParseExpression(PrecUnary);
                    return new UnaryCallNode(token.Lexeme, operand, token.Line, token.Column);
                }

            // Identifier — could be keyword, variable, nular command
            case TokenType.Identifier:
                return ParseIdentifier(token);

            default:
                throw NewError($"Unexpected token: {token.Lexeme}");
        }
    }

    private AstNode ParseIdentifier(Token token)
    {
        string name = token.Lexeme;

        // Boolean literals
        if (string.Equals(name, "true", StringComparison.OrdinalIgnoreCase))
            return new BoolLiteralNode(true, token.Line, token.Column);
        if (string.Equals(name, "false", StringComparison.OrdinalIgnoreCase))
            return new BoolLiteralNode(false, token.Line, token.Column);
        if (string.Equals(name, "nil", StringComparison.OrdinalIgnoreCase))
            return new NilLiteralNode(token.Line, token.Column);

        // Keywords — control structures (parsed as sugar)
        if (string.Equals(name, "if", StringComparison.OrdinalIgnoreCase))
            return ParseIf();
        if (string.Equals(name, "while", StringComparison.OrdinalIgnoreCase))
            return ParseWhile();
        if (string.Equals(name, "for", StringComparison.OrdinalIgnoreCase))
            return ParseFor();
        if (string.Equals(name, "switch", StringComparison.OrdinalIgnoreCase))
            return ParseSwitch();

        // call / spawn
        if (string.Equals(name, "call", StringComparison.OrdinalIgnoreCase))
            return ParseCallSpawn(isCall: true);
        if (string.Equals(name, "spawn", StringComparison.OrdinalIgnoreCase))
            return ParseCallSpawn(isCall: false);

        // import
        if (string.Equals(name, "import", StringComparison.OrdinalIgnoreCase))
            return ParseImport();

        // try/catch
        if (string.Equals(name, "try", StringComparison.OrdinalIgnoreCase))
            return ParseTryCatch();

        // return
        if (string.Equals(name, "return", StringComparison.OrdinalIgnoreCase))
        {
            var val = IsAtEnd() || Peek().Type == TokenType.Semicolon ? null : ParseExpression();
            return new ReturnNode(val, token.Line, token.Column);
        }

        // global keyword — must be followed by assignment
        if (string.Equals(name, "global", StringComparison.OrdinalIgnoreCase))
        {
            var varToken = Expect(TokenType.Identifier, "Expected variable name after 'global'");
            Expect(TokenType.Assign, "Expected '=' after global variable");
            var value = ParseExpression();
            return new AssignmentNode(varToken.Lexeme, value, token.Line, token.Column)
            {
                IsGlobal = true
            };
        }

        // private keyword
        if (string.Equals(name, "private", StringComparison.OrdinalIgnoreCase))
            return ParsePrivate(token);

        // shared keyword — CAS-based synchronized variable
        if (string.Equals(name, "shared", StringComparison.OrdinalIgnoreCase))
            return ParseShared(token);

        // throw keyword — error propagation
        if (string.Equals(name, "throw", StringComparison.OrdinalIgnoreCase))
            return ParseThrow(token);

        // Variable or nular command (resolved at semantic analysis)
        // Unary vs nular disambiguation happens in the expression loop
        return new VariableNode(name, token.Line, token.Column);
    }

    private static bool CanStartAtomicExpression(TokenType type)
    {
        // Tokens that begin an atomic (non-operator) expression
        return type == TokenType.Number
            || type == TokenType.String
            || type == TokenType.CodeBlock
            || type == TokenType.LParen
            || type == TokenType.LBracket
            || type == TokenType.Identifier;
    }

    // --- Infix parsing ---
    private AstNode ParseInfix(AstNode left, int precedence)
    {
        Token token = Advance();

        // Hash-select: arr # index (prec 9)
        if (token.Type == TokenType.Hash)
        {
            var index = ParseExpression(PrecHash);
            return new BinaryCallNode(left, "#", index, PrecHash, token.Line, token.Column);
        }

        // Assignment: var = expr
        if (token.Type == TokenType.Assign)
        {
            if (left is VariableNode varNode)
            {
                var value = ParseExpression(precedence);
                return new AssignmentNode(varNode.Name, value, token.Line, token.Column);
            }
            throw NewError("Left side of assignment must be a variable");
        }

        // Colon (switch case, not yet used elsewhere)
        if (token.Type == TokenType.Colon)
        {
            var right = ParseExpression(precedence);
            return new BinaryCallNode(left, ":", right, precedence, token.Line, token.Column);
        }

        // Identifier as infix binary operator
        if (token.Type == TokenType.Identifier)
        {
            string op = token.Lexeme;
            if (string.Equals(op, "and", StringComparison.OrdinalIgnoreCase))
                return ParseInfixBinary(left, "&&", PrecAnd, token);
            if (string.Equals(op, "or", StringComparison.OrdinalIgnoreCase))
                return ParseInfixBinary(left, "||", PrecOr, token);
            if (string.Equals(op, "mod", StringComparison.OrdinalIgnoreCase))
                return ParseInfixBinary(left, "%", PrecMulDiv, token);

            // call / spawn in infix position: args call {code} / args spawn {code}
            if (string.Equals(op, "call", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op, "spawn", StringComparison.OrdinalIgnoreCase))
            {
                bool isCall = string.Equals(op, "call", StringComparison.OrdinalIgnoreCase);
                AstNode code;
                if (Match(TokenType.CodeBlock))
                {
                    var body = ParseExpressionFromTokens(_tokens[_pos - 1].NestedTokens!);
                    code = new CodeLiteralNode(
                        body is SequenceNode s ? s.Expressions : new List<AstNode> { body },
                        _tokens[_pos - 1].Line, _tokens[_pos - 1].Column);
                }
                else
                {
                    code = ParseExpression(precedence);
                }
                return isCall
                    ? new CallNode(left, code, token.Line, token.Column)
                    : new SpawnNode(left, code, token.Line, token.Column);
            }

            var rightExpr = ParseExpression(precedence);
            return new BinaryCallNode(left, op, rightExpr, precedence, token.Line, token.Column);
        }

        // Standard symbolic binary operator
        var rhs = ParseExpression(precedence);
        return new BinaryCallNode(left, token.Lexeme, rhs, precedence, token.Line, token.Column);
    }

    // --- Control structure parsing ---
    private AstNode ParseIf()
    {
        int line = _previous.Line;
        int col = _previous.Column;

        // Condition: if (expr) or if expr
        AstNode condition;
        if (Match(TokenType.LParen))
        {
            condition = ParseExpression();
            Expect(TokenType.RParen, "Expected ')' after condition");
        }
        else
        {
            condition = ParseExpression();
        }

        // Optional 'then'
        if (MatchIdentifier("then")) { /* skip, sugar */ }

        // Then body (must be code block or single expr)
        AstNode thenBody = ParseBodyOrExpr();

        // Optional else
        AstNode? elseBody = null;
        if (MatchIdentifier("else"))
        {
            elseBody = ParseBodyOrExpr();
        }

        // Semicolon after if block (SQF convention)
        Match(TokenType.Semicolon);

        return new IfThenElseNode(condition, thenBody, elseBody, line, col);
    }

    private AstNode ParseWhile()
    {
        int line = _previous.Line;
        int col = _previous.Column;

        AstNode condition;
        if (Match(TokenType.CodeBlock))
        {
            condition = new CodeLiteralNode(
                new List<AstNode> { ParseExpressionFromTokens(_tokens[_pos - 1].NestedTokens!) },
                _tokens[_pos - 1].Line, _tokens[_pos - 1].Column);
        }
        else
        {
            condition = ParseExpression();
        }

        ExpectIdentifier("do", "Expected 'do' after while condition");

        AstNode body = ParseBodyOrExpr();
        Match(TokenType.Semicolon);

        return new WhileDoNode(condition, body, line, col);
    }

    private AstNode ParseFor()
    {
        int line = _previous.Line;
        int col = _previous.Column;

        // Check for from-to-step form: for "VARNAME" from ...
        if (Match(TokenType.String))
        {
            string varName = _tokens[_pos - 1].StringValue!;
            ExpectIdentifier("from", "Expected 'from' after variable name");
            var from = ParseExpression();
            ExpectIdentifier("to", "Expected 'to' after from value");
            var to = ParseExpression();
            AstNode? step = null;
            if (MatchIdentifier("step"))
                step = ParseExpression();
            ExpectIdentifier("do", "Expected 'do' after for header");
            var body = ParseBodyOrExpr();
            Match(TokenType.Semicolon);
            return new ForFromToNode(varName, from, to, step, body, line, col);
        }

        // Array form: for [{INIT}, {COND}, {STEP}] do {BODY}
        Expect(TokenType.LBracket, "Expected '[' or string for for-loop");
        var init = Match(TokenType.CodeBlock)
            ? new CodeLiteralNode(new List<AstNode>(), _tokens[_pos - 1].Line, _tokens[_pos - 1].Column)
            : ParseExpression();
        if (Match(TokenType.Comma)) { /* skip */ }
        var cond = Match(TokenType.CodeBlock)
            ? new CodeLiteralNode(new List<AstNode>(), _tokens[_pos - 1].Line, _tokens[_pos - 1].Column)
            : ParseExpression();
        if (Match(TokenType.Comma)) { /* skip */ }
        var stepBody = Match(TokenType.CodeBlock)
            ? new CodeLiteralNode(new List<AstNode>(), _tokens[_pos - 1].Line, _tokens[_pos - 1].Column)
            : ParseExpression();
        Expect(TokenType.RBracket, "Expected ']'");
        ExpectIdentifier("do", "Expected 'do' after for header");
        var forBody = ParseBodyOrExpr();
        Match(TokenType.Semicolon);
        return new ForDoNode(init, cond, stepBody, forBody, line, col);
    }

    private AstNode ParseSwitch()
    {
        int line = _previous.Line;
        int col = _previous.Column;

        AstNode value;
        if (Match(TokenType.LParen))
        {
            value = ParseExpression();
            Expect(TokenType.RParen, "Expected ')'");
        }
        else
        {
            value = ParseExpression();
        }

        ExpectIdentifier("do", "Expected 'do' after switch value");
        Expect(TokenType.CodeBlock, "Expected code block for switch cases");

        // Parse case/default statements from the nested token block
        var caseTokens = _tokens[_pos - 1].NestedTokens!;
        var cases = ParseSwitchCases(caseTokens, line);
        Match(TokenType.Semicolon);
        return new SwitchDoNode(value, cases, line, col);
    }

    private List<SwitchCase> ParseSwitchCases(List<Token> tokens, int parentLine)
    {
        var innerParser = new Parser(tokens);
        var cases = new List<SwitchCase>();
        while (!innerParser.IsAtEnd())
        {
            // Skip semicolons and commas between cases
            innerParser.Match(TokenType.Semicolon);
            innerParser.Match(TokenType.Comma);
            if (innerParser.IsAtEnd()) break;

            if (innerParser.MatchIdentifier("default"))
            {
                innerParser.Match(TokenType.Colon);
                var body = innerParser.ParseBodyOrExpr();
                cases.Add(new SwitchCase(null, body));
            }
            else if (innerParser.MatchIdentifier("case"))
            {
                // Parse case value at high precedence to prevent colon being consumed as binary op
                var caseValue = innerParser.ParseExpression(PrecBinary);
                innerParser.Match(TokenType.Colon);
                var body = innerParser.ParseBodyOrExpr();
                cases.Add(new SwitchCase(caseValue, body));
            }
            else
            {
                // Unexpected token in switch block — try to recover
                innerParser.Advance();
            }
        }
        return cases;
    }

    // --- Helpers ---
    private AstNode ParseArray()
    {
        int line = _tokens[_pos - 1].Line;
        int col = _tokens[_pos - 1].Column;
        var elements = new List<AstNode>();

        if (!Check(TokenType.RBracket))
        {
            do
            {
                elements.Add(ParseExpression());
            } while (Match(TokenType.Comma));
        }
        Expect(TokenType.RBracket, "Expected ']'");
        return new ArrayExprNode(elements, line, col);
    }

    private AstNode ParseCallSpawn(bool isCall)
    {
        int line = _previous.Line;
        int col = _previous.Column;

        // Check for args before call/spawn: args call code
        AstNode? args = null;
        if (!Check(TokenType.CodeBlock) && !Check(TokenType.String))
        {
            args = ParseExpression(PrecBinary);
        }

        AstNode code;
        if (Match(TokenType.CodeBlock))
        {
            var body = ParseExpressionFromTokens(_tokens[_pos - 1].NestedTokens!);
            code = new CodeLiteralNode(
                body is SequenceNode s ? s.Expressions : new List<AstNode> { body },
                _tokens[_pos - 1].Line, _tokens[_pos - 1].Column);
        }
        else
        {
            code = ParseExpression(PrecBinary);
        }

        return isCall
            ? new CallNode(args, code, line, col)
            : new SpawnNode(args, code, line, col);
    }

    private AstNode ParseImport()
    {
        int line = _previous.Line;
        int col = _previous.Column;
        var pathToken = Expect(TokenType.String, "Expected file path string after 'import'");
        return new ImportNode(pathToken.StringValue!, line, col);
    }

    private AstNode ParseTryCatch()
    {
        int line = _previous.Line;
        int col = _previous.Column;
        var tryBody = ParseBodyOrExpr();
        ExpectIdentifier("catch", "Expected 'catch' after try block");
        var catchBody = ParseBodyOrExpr();
        Match(TokenType.Semicolon);
        return new TryCatchNode(tryBody, catchBody, "_exception", line, col);
    }

    private AstNode ParsePrivate(Token token)
    {
        // private _var = value
        var varToken = Expect(TokenType.Identifier, "Expected variable name after 'private'");
        if (Match(TokenType.Assign))
        {
            var value = ParseExpression();
            return new AssignmentNode(varToken.Lexeme, value, token.Line, token.Column)
            {
                // In semantic analysis, mark as local declaration
            };
        }
        return new VariableNode(varToken.Lexeme, varToken.Line, varToken.Column);
    }

    private AstNode ParseShared(Token token)
    {
        // shared _var = expr
        var varToken = Expect(TokenType.Identifier, "Expected variable name after 'shared'");
        Expect(TokenType.Assign, "Expected '=' after variable name in shared declaration");
        var value = ParseExpression();
        return new SharedDeclarationNode(varToken.Lexeme, value, token.Line, token.Column);
    }

    private AstNode ParseThrow(Token token)
    {
        // throw expr
        var value = ParseExpression();
        return new UnaryCallNode("throw", value, token.Line, token.Column);
    }

    private AstNode ParseBodyOrExpr()
    {
        if (Match(TokenType.CodeBlock))
        {
            var body = ParseExpressionFromTokens(_tokens[_pos - 1].NestedTokens!);
            return new CodeLiteralNode(
                body is SequenceNode s ? s.Expressions : new List<AstNode> { body },
                _tokens[_pos - 1].Line, _tokens[_pos - 1].Column);
        }
        return ParseExpression();
    }

    private static AstNode ParseExpressionFromTokens(List<Token> tokens)
    {
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    // --- Token helpers ---
    private Token Peek() => _tokens[_pos];
    private Token Advance() => _previous = _tokens[_pos++];
    private bool IsAtEnd() => _pos >= _tokens.Count || _tokens[_pos].Type == TokenType.Eof;
    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

    private bool Match(TokenType type)
    {
        if (Check(type))
        {
            Advance();
            return true;
        }
        return false;
    }

    private bool MatchIdentifier(string name)
    {
        if (!IsAtEnd() && Peek().Type == TokenType.Identifier &&
            string.Equals(Peek().Lexeme, name, StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            return true;
        }
        return false;
    }

    private Token Expect(TokenType type, string errorMessage)
    {
        if (Check(type))
            return Advance();
        throw NewError(errorMessage);
    }

    private void ExpectIdentifier(string name, string errorMessage)
    {
        if (!MatchIdentifier(name))
            throw NewError(errorMessage);
    }

    private Exception NewError(string message)
    {
        var token = IsAtEnd() ? _tokens[^1] : Peek();
        return new Exception($"Parse error at line {token.Line}, col {token.Column}: {message}");
    }

    private AstNode ParseInfixBinary(AstNode left, string op, int prec, Token token)
    {
        var right = ParseExpression(prec);
        return new BinaryCallNode(left, op, right, prec, token.Line, token.Column);
    }

    private int GetEffectivePrecedence(Token token)
    {
        if (token.Type == TokenType.Identifier)
        {
            string name = token.Lexeme;
            if (IsExpressionBreaker(name)) return PrecNone;
            if (string.Equals(name, "and", StringComparison.OrdinalIgnoreCase)) return PrecAnd;
            if (string.Equals(name, "or", StringComparison.OrdinalIgnoreCase)) return PrecOr;
            if (string.Equals(name, "mod", StringComparison.OrdinalIgnoreCase)) return PrecMulDiv;
            if (string.Equals(name, "call", StringComparison.OrdinalIgnoreCase)) return PrecCall;
            if (string.Equals(name, "spawn", StringComparison.OrdinalIgnoreCase)) return PrecCall;
            // Generic binary command — but only if followed by something that
            // can be its right operand (not another identifier that would make
            // this look like two consecutive nular/unary calls)
            return PrecBinary;
        }
        return token.Type switch
        {
            TokenType.OrOr => PrecOr,
            TokenType.AndAnd => PrecAnd,
            TokenType.EqEq or TokenType.NotEq or TokenType.Less or TokenType.Greater
                or TokenType.LessEq or TokenType.GreaterEq => PrecComparison,
            TokenType.Hash => PrecHash,
            TokenType.Caret => PrecPower,
            TokenType.Star or TokenType.Slash or TokenType.Percent => PrecMulDiv,
            TokenType.Plus or TokenType.Minus => PrecAddSub,
            TokenType.Assign => PrecComparison,
            TokenType.Colon => PrecBinary,
            _ => PrecNone
        };
    }

    /// <summary>
    /// Keywords that break the expression infix loop.
    /// Includes expression-start keywords AND control-flow chaining keywords.
    /// </summary>
    private static bool IsExpressionBreaker(string name)
    {
        return name.Equals("if", StringComparison.OrdinalIgnoreCase)
            || name.Equals("while", StringComparison.OrdinalIgnoreCase)
            || name.Equals("for", StringComparison.OrdinalIgnoreCase)
            || name.Equals("switch", StringComparison.OrdinalIgnoreCase)
            || name.Equals("import", StringComparison.OrdinalIgnoreCase)
            || name.Equals("try", StringComparison.OrdinalIgnoreCase)
            || name.Equals("return", StringComparison.OrdinalIgnoreCase)
            || name.Equals("global", StringComparison.OrdinalIgnoreCase)
            || name.Equals("private", StringComparison.OrdinalIgnoreCase)
            || name.Equals("shared", StringComparison.OrdinalIgnoreCase)
            // Control-flow chaining keywords — break expression, handled by control parsers
            || name.Equals("then", StringComparison.OrdinalIgnoreCase)
            || name.Equals("else", StringComparison.OrdinalIgnoreCase)
            || name.Equals("do", StringComparison.OrdinalIgnoreCase)
            || name.Equals("from", StringComparison.OrdinalIgnoreCase)
            || name.Equals("to", StringComparison.OrdinalIgnoreCase)
            || name.Equals("case", StringComparison.OrdinalIgnoreCase)
            || name.Equals("default", StringComparison.OrdinalIgnoreCase);
    }
}

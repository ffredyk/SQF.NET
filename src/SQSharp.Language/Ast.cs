namespace SQSharp.Language;

/// <summary>
/// AST node types for SQ# expressions and statements.
/// Note: In SQF/SQ#, everything is an expression. There are no pure statements.
/// </summary>
public enum AstNodeType
{
    // Literals
    NilLiteral,
    BoolLiteral,
    NumberLiteral,
    StringLiteral,
    CodeLiteral,

    // Values
    Variable,       // _localVar or globalVar
    ArrayExpr,      // [elem, elem, ...]

    // Operators (arity-dispatched)
    NularCall,      // bareWord — nular command or variable lookup
    UnaryCall,      // operator rightExpr
    BinaryCall,     // leftExpr operator rightExpr
    Assignment,     // variable = expr

    // Control flow (desugared to operator chains internally)
    IfThenElse,
    WhileDo,
    ForDo,          // for [{...},{...},{...}] do {...}
    ForFromTo,      // for "var" from start to end [step s] do {...}
    SwitchDo,

    // Code execution
    Call,           // args call code
    Spawn,          // args spawn code

    // Sequence
    Sequence,       // expr; expr; expr — returns last value

    // SQ# additions
    Import,         // import "module"
    TryCatch,
    Return,
    SharedDeclaration,  // shared _var = expr
}

/// <summary>
/// Base class for all AST nodes.
/// </summary>
public abstract class AstNode
{
    public AstNodeType NodeType { get; }
    public int Line { get; }
    public int Column { get; }

    protected AstNode(AstNodeType type, int line, int column)
    {
        NodeType = type;
        Line = line;
        Column = column;
    }
}

// --- Literals ---

public sealed class NilLiteralNode : AstNode
{
    public NilLiteralNode(int line, int col) : base(AstNodeType.NilLiteral, line, col) { }
}

public sealed class BoolLiteralNode : AstNode
{
    public bool Value { get; }
    public BoolLiteralNode(bool value, int line, int col) : base(AstNodeType.BoolLiteral, line, col)
    {
        Value = value;
    }
}

public sealed class NumberLiteralNode : AstNode
{
    public double Value { get; }
    public NumberLiteralNode(double value, int line, int col) : base(AstNodeType.NumberLiteral, line, col)
    {
        Value = value;
    }
}

public sealed class StringLiteralNode : AstNode
{
    public string Value { get; }
    public StringLiteralNode(string value, int line, int col) : base(AstNodeType.StringLiteral, line, col)
    {
        Value = value;
    }
}

public sealed class CodeLiteralNode : AstNode
{
    public List<AstNode> Body { get; }
    public CodeLiteralNode(List<AstNode> body, int line, int col)
        : base(AstNodeType.CodeLiteral, line, col)
    {
        Body = body;
    }
}

// --- Values ---

public sealed class VariableNode : AstNode
{
    public string Name { get; }
    public bool IsLocal => Name.StartsWith("_");
    public VariableNode(string name, int line, int col) : base(AstNodeType.Variable, line, col)
    {
        Name = name;
    }
}

public sealed class ArrayExprNode : AstNode
{
    public List<AstNode> Elements { get; }
    public ArrayExprNode(List<AstNode> elements, int line, int col)
        : base(AstNodeType.ArrayExpr, line, col)
    {
        Elements = elements;
    }
}

// --- Operator Calls ---

public sealed class NularCallNode : AstNode
{
    public string CommandName { get; }
    public NularCallNode(string name, int line, int col) : base(AstNodeType.NularCall, line, col)
    {
        CommandName = name;
    }
}

public sealed class UnaryCallNode : AstNode
{
    public string Operator { get; }
    public AstNode Operand { get; }
    public UnaryCallNode(string op, AstNode operand, int line, int col)
        : base(AstNodeType.UnaryCall, line, col)
    {
        Operator = op;
        Operand = operand;
    }
}

public sealed class BinaryCallNode : AstNode
{
    public AstNode Left { get; }
    public string Operator { get; }
    public AstNode Right { get; }

    /// <summary>Precedence level 1-11 for this operator.</summary>
    public int Precedence { get; }

    public BinaryCallNode(AstNode left, string op, AstNode right, int precedence, int line, int col)
        : base(AstNodeType.BinaryCall, line, col)
    {
        Left = left;
        Operator = op;
        Right = right;
        Precedence = precedence;
    }
}

public sealed class AssignmentNode : AstNode
{
    public string VariableName { get; }
    public AstNode Value { get; }
    public bool IsLocal => VariableName.StartsWith("_");
    public bool IsGlobal { get; set; } // true if 'global' keyword prefixed

    public AssignmentNode(string name, AstNode value, int line, int col)
        : base(AstNodeType.Assignment, line, col)
    {
        VariableName = name;
        Value = value;
    }
}

// --- Control Flow ---

public sealed class IfThenElseNode : AstNode
{
    public AstNode Condition { get; }
    public AstNode ThenBody { get; }
    public AstNode? ElseBody { get; }

    public IfThenElseNode(AstNode condition, AstNode thenBody, AstNode? elseBody, int line, int col)
        : base(AstNodeType.IfThenElse, line, col)
    {
        Condition = condition;
        ThenBody = thenBody;
        ElseBody = elseBody;
    }
}

public sealed class WhileDoNode : AstNode
{
    public AstNode Condition { get; } // must be Code literal
    public AstNode Body { get; }       // must be Code literal

    public WhileDoNode(AstNode condition, AstNode body, int line, int col)
        : base(AstNodeType.WhileDo, line, col)
    {
        Condition = condition;
        Body = body;
    }
}

public sealed class ForDoNode : AstNode
{
    public AstNode Init { get; }
    public AstNode Condition { get; }
    public AstNode Step { get; }
    public AstNode Body { get; }

    public ForDoNode(AstNode init, AstNode condition, AstNode step, AstNode body, int line, int col)
        : base(AstNodeType.ForDo, line, col)
    {
        Init = init;
        Condition = condition;
        Step = step;
        Body = body;
    }
}

public sealed class ForFromToNode : AstNode
{
    public string VarName { get; }
    public AstNode From { get; }
    public AstNode To { get; }
    public AstNode? Step { get; }
    public AstNode Body { get; }

    public ForFromToNode(string varName, AstNode from, AstNode to, AstNode? step, AstNode body, int line, int col)
        : base(AstNodeType.ForFromTo, line, col)
    {
        VarName = varName;
        From = from;
        To = to;
        Step = step;
        Body = body;
    }
}

public sealed class SwitchDoNode : AstNode
{
    /// <summary>The value being switched on.</summary>
    public AstNode Value { get; }

    /// <summary>List of (caseValue, body) pairs. caseValue may be null for default.</summary>
    public List<SwitchCase> Cases { get; }

    public SwitchDoNode(AstNode value, List<SwitchCase> cases, int line, int col)
        : base(AstNodeType.SwitchDo, line, col)
    {
        Value = value;
        Cases = cases;
    }
}

/// <summary>A single case or default within a switch block.</summary>
public sealed class SwitchCase
{
    /// <summary>The value to match against. Null means 'default'.</summary>
    public AstNode? CaseValue { get; }

    /// <summary>The body to execute if this case matches.</summary>
    public AstNode Body { get; }

    public SwitchCase(AstNode? caseValue, AstNode body)
    {
        CaseValue = caseValue;
        Body = body;
    }
}

// --- Execution ---

public sealed class CallNode : AstNode
{
    public AstNode? Arguments { get; }
    public AstNode Code { get; }

    public CallNode(AstNode? args, AstNode code, int line, int col)
        : base(AstNodeType.Call, line, col)
    {
        Arguments = args;
        Code = code;
    }
}

public sealed class SpawnNode : AstNode
{
    public AstNode? Arguments { get; }
    public AstNode Code { get; }

    public SpawnNode(AstNode? args, AstNode code, int line, int col)
        : base(AstNodeType.Spawn, line, col)
    {
        Arguments = args;
        Code = code;
    }
}

public sealed class SequenceNode : AstNode
{
    public List<AstNode> Expressions { get; }

    public SequenceNode(List<AstNode> expressions, int line, int col)
        : base(AstNodeType.Sequence, line, col)
    {
        Expressions = expressions;
    }
}

// --- SQ# Additions ---

public sealed class ImportNode : AstNode
{
    public string Path { get; }
    public ImportNode(string path, int line, int col) : base(AstNodeType.Import, line, col)
    {
        Path = path;
    }
}

public sealed class TryCatchNode : AstNode
{
    public AstNode TryBody { get; }
    public AstNode CatchBody { get; }
    public string? ErrorVariable { get; }

    public TryCatchNode(AstNode tryBody, AstNode catchBody, string? errorVar, int line, int col)
        : base(AstNodeType.TryCatch, line, col)
    {
        TryBody = tryBody;
        CatchBody = catchBody;
        ErrorVariable = errorVar;
    }
}

public sealed class ReturnNode : AstNode
{
    public AstNode? Value { get; }
    public ReturnNode(AstNode? value, int line, int col) : base(AstNodeType.Return, line, col)
    {
        Value = value;
    }
}

/// <summary>
/// Shared variable declaration: shared _var = expr.
/// Creates a CAS-based synchronized mutable value accessible across schedulers.
/// </summary>
public sealed class SharedDeclarationNode : AstNode
{
    /// <summary>Variable name (e.g., "_counter").</summary>
    public string VariableName { get; }

    /// <summary>Initializer expression.</summary>
    public AstNode Initializer { get; }

    /// <summary>Whether the variable is local (starts with _).</summary>
    public bool IsLocal => VariableName.StartsWith('_');

    public SharedDeclarationNode(string variableName, AstNode initializer, int line, int col)
        : base(AstNodeType.SharedDeclaration, line, col)
    {
        VariableName = variableName;
        Initializer = initializer;
    }
}

using System;

namespace SQSharp.Core;

/// <summary>
/// Runtime error in SQ# script execution.
/// </summary>
public class SqError
{
    public string Message { get; }
    public string? SourceFile { get; }
    public int Line { get; }
    public int Column { get; }
    public string? StackTrace { get; }

    public SqError(string message, string? sourceFile = null,
        int line = 0, int column = 0, string? stackTrace = null)
    {
        Message = message;
        SourceFile = sourceFile;
        Line = line;
        Column = column;
        StackTrace = stackTrace;
    }

    public SqValue ToValue() => new(SqType.Error, this);

    public override string ToString() =>
        SourceFile != null
            ? $"{SourceFile}({Line},{Column}): {Message}"
            : Message;
}

/// <summary>
/// Thrown when a value of unexpected type is encountered.
/// </summary>
public class SqTypeError : Exception
{
    public SqTypeError(string message) : base(message) { }
}

/// <summary>
/// Thrown when an undefined variable is accessed in strict mode.
/// </summary>
public class SqUndefinedVariableError : Exception
{
    public string VariableName { get; }

    public SqUndefinedVariableError(string message)
        : base(message)
    {
        VariableName = message;
    }
}

/// <summary>
/// Thrown when cross-scheduler ownership is violated.
/// </summary>
public class SqOwnershipError : Exception
{
    public SqOwnershipError(string message) : base(message) { }
}

/// <summary>
/// Thrown when a thread-unsafe command is called from wrong scheduler.
/// </summary>
public class SqThreadSafetyError : Exception
{
    public SqThreadSafetyError(string message) : base(message) { }
}

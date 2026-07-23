using SQSharp.Core;

namespace SQSharp.VM;

/// <summary>
/// Minimal scheduler interface that the VM needs for spawn/sleep/yield/await.
/// </summary>
public interface ISqScheduler
{
    /// <summary>Current scheduler time in seconds.</summary>
    double CurrentTime { get; }

    /// <summary>Spawn a new fiber from a bytecode chunk. Returns fiber handle.</summary>
    object Spawn(BytecodeChunk chunk, string name, SqValue[]? args);

    /// <summary>Spawn a new fiber on a named scheduler. Returns fiber handle.</summary>
    object SpawnOn(string schedulerName, BytecodeChunk chunk, string name, SqValue[]? args);

    /// <summary>Suspend current fiber for N seconds.</summary>
    void SleepCurrent(double seconds);

    /// <summary>Yield current fiber back to ready queue.</summary>
    void YieldCurrent();

    /// <summary>Suspend current fiber until handle resolves or timeout expires.</summary>
    void AwaitHandle(object handle, double timeoutSeconds);

    /// <summary>Terminate (resolve) a script handle.</summary>
    void TerminateHandle(object handle, SqValue? result);

    /// <summary>Check if a script handle is resolved.</summary>
    bool IsHandleResolved(object handle);

    /// <summary>
    /// Create a new handle that resolves with the original handle's value,
    /// or nil after timeoutSeconds. Whichever happens first wins.
    /// </summary>
    object ScheduleTimeout(object handle, double timeoutSeconds);

    /// <summary>Handle print output from script.</summary>
    void Print(string message);

    /// <summary>Unique ID of this scheduler (for ownership tracking).</summary>
    int SchedulerId { get; }

    /// <summary>Maximum iterations for forEach/while loops. 0 = unlimited.</summary>
    int MaxIterations { get; }

    /// <summary>Execute code directly (unscheduled, bypassing fiber queue).</summary>
    SqValue CallUnscheduled(BytecodeChunk chunk, SqValue[]? args);
}

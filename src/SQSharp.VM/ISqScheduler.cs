using SQSharp.Core;

namespace SQSharp.VM;

/// <summary>
/// Minimal scheduler interface that the VM needs for spawn/sleep/yield.
/// </summary>
public interface ISqScheduler
{
    /// <summary>Current scheduler time in seconds.</summary>
    double CurrentTime { get; }

    /// <summary>Spawn a new fiber from a bytecode chunk. Returns fiber handle.</summary>
    object Spawn(BytecodeChunk chunk, string name, SqValue[]? args);

    /// <summary>Suspend current fiber for N seconds.</summary>
    void SleepCurrent(double seconds);

    /// <summary>Yield current fiber back to ready queue.</summary>
    void YieldCurrent();

    /// <summary>Handle print output from script.</summary>
    void Print(string message);

    /// <summary>Unique ID of this scheduler (for ownership tracking).</summary>
    int SchedulerId { get; }
}

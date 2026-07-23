using System;
using SQSharp.Core;

namespace SQSharp.Scheduler;

/// <summary>
/// Execution state of a fiber.
/// </summary>
public enum FiberState
{
    /// <summary>Ready to run.</summary>
    Ready,
    /// <summary>Currently executing.</summary>
    Running,
    /// <summary>Waiting on a condition (sleep, promise, etc.).</summary>
    Waiting,
    /// <summary>Completed execution.</summary>
    Completed,
    /// <summary>Terminated by external request.</summary>
    Terminated,
}

/// <summary>
/// A lightweight cooperative execution context — one "script thread."
/// Each fiber has its own VM, stack, and local variables.
/// </summary>
public class SqFiber
{
    private static int _nextId = 1;

    public int Id { get; }
    public string Name { get; }
    public FiberState State { get; internal set; } = FiberState.Ready;

    /// <summary>The VM executing this fiber's bytecode.</summary>
    public VM.SqVm Vm { get; }

    /// <summary>The scheduler this fiber belongs to.</summary>
    public SqScheduler Scheduler { get; }

    /// <summary>Time this fiber was created (scheduler time).</summary>
    public double CreatedAt { get; }

    /// <summary>Accumulated execution time in milliseconds.</summary>
    public double TotalExecutionMs { get; internal set; }

    /// <summary>Result value when fiber completes.</summary>
    public SqValue? Result { get; internal set; }

    /// <summary>Promise handle for this fiber (resolved on completion).</summary>
    public ScriptHandle Handle { get; }

    /// <summary>Time when waiting period ends (for sleep). 0 = not waiting.</summary>
    internal double WaitUntil { get; set; }

    /// <summary>Optional callback when fiber completes.</summary>
    internal Action<SqValue?>? OnComplete { get; set; }

    public SqFiber(string name, VM.SqVm vm, SqScheduler scheduler, double currentTime)
    {
        Id = _nextId++;
        Name = name;
        Vm = vm;
        Scheduler = scheduler;
        CreatedAt = currentTime;
        Handle = new ScriptHandle(this);
    }

    /// <summary>
    /// Mark fiber as waiting for a duration (sleep).
    /// </summary>
    public void Sleep(double seconds)
    {
        WaitUntil = Scheduler.CurrentTime + seconds;
        State = FiberState.Waiting;
    }

    /// <summary>
    /// Mark fiber as waiting on a promise.
    /// </summary>
    public void WaitFor(ScriptHandle handle)
    {
        State = FiberState.Waiting;
        handle.OnResolved += _ => State = FiberState.Ready;
    }

    public override string ToString() => $"Fiber#{Id} '{Name}' [{State}]";
}

/// <summary>
/// A handle to a spawned script — used as a promise.
/// </summary>
public class ScriptHandle
{
    private readonly SqFiber? _fiber;
    private SqValue? _resolvedValue;
    private bool _resolved;

    public bool IsResolved => _resolved || (_fiber?.State == FiberState.Completed);
    public event Action<SqValue?>? OnResolved;

    /// <summary>The resolved value, or null if not yet resolved.</summary>
    public SqValue? ResolvedValue => GetResult();

    internal ScriptHandle(SqFiber? fiber = null)
    {
        _fiber = fiber;
    }

    /// <summary>
    /// Resolve this promise with a value.
    /// </summary>
    public void Resolve(SqValue? value)
    {
        _resolvedValue = value;
        _resolved = true;
        OnResolved?.Invoke(value);
    }

    /// <summary>
    /// Get the resolved value, blocking the current fiber until ready.
    /// Returns null if not yet resolved.
    /// </summary>
    public SqValue? GetResult()
    {
        if (_resolved) return _resolvedValue;
        if (_fiber?.State == FiberState.Completed)
            return _fiber.Result;
        return null;
    }
}

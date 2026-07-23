using System;
using System.Collections.Generic;
using System.Diagnostics;
using SQSharp.Core;
using SQSharp.VM;

namespace SQSharp.Scheduler;

/// <summary>
/// Cooperative scheduler that manages fibers with a time budget per tick.
/// Multiple schedulers can run on different threads.
/// </summary>
public class SqScheduler : ISqScheduler
{
    private static int _nextSchedulerId = 1;

    /// <summary>Global registry mapping scheduler names to instances (for SpawnOn).</summary>
    private static readonly Dictionary<string, SqScheduler> _registry = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _name;
    private readonly int _schedulerId;
    private readonly Queue<SqFiber> _readyQueue = new();
    private readonly List<SqFiber> _waitingFibers = new();
    private readonly List<SqFiber> _completedFibers = new();
    private SqFiber? _activeFiber;
    private readonly Stopwatch _stopwatch = new();

    // Pending timeout handles: (newHandle, expiryTime, sourceHandle)
    private readonly List<(ScriptHandle handle, double expiry, ScriptHandle source)> _pendingTimeouts = new();

    // Command registries (injected by host for spawned scripts)
    public Dictionary<string, Func<SqValue>>? NularCommands { get; set; }
    public Dictionary<string, Func<SqValue, SqValue>>? UnaryCommands { get; set; }
    public Dictionary<string, Func<SqValue, SqValue, SqValue>>? BinaryCommands { get; set; }

    int ISqScheduler.SchedulerId => _schedulerId;

    /// <summary>Public scheduler ID (matches ISqScheduler.SchedulerId).</summary>
    public int SchedulerId => _schedulerId;

    /// <summary>Maximum milliseconds to run fibers per tick.</summary>
    public double TimeBudgetMs { get; set; } = 3.0;

    /// <summary>Maximum loop iterations for forEach/while. 0 = unlimited. Host-configurable.</summary>
    public int MaxIterations { get; set; } = 0;

    int ISqScheduler.MaxIterations => MaxIterations;

    /// <summary>Current scheduler time in seconds.</summary>
    public double CurrentTime => _stopwatch.Elapsed.TotalSeconds;

    /// <summary>Total number of fibers created by this scheduler.</summary>
    public int TotalFibersCreated { get; private set; }

    /// <summary>Number of fibers currently ready to run.</summary>
    public int ReadyCount => _readyQueue.Count;

    /// <summary>Number of fibers currently waiting.</summary>
    public int WaitingCount => _waitingFibers.Count;

    /// <summary>Is the scheduler currently executing a tick?</summary>
    public bool IsRunning { get; private set; }

    public SqScheduler(string name = "Main")
    {
        _name = name;
        _schedulerId = _nextSchedulerId++;
        _registry[name] = this;
    }

    /// <summary>Look up a scheduler by name from the global registry.</summary>
    public static SqScheduler? Find(string name) =>
        _registry.TryGetValue(name, out var s) ? s : null;

    /// <summary>
    /// Spawn a new fiber from a bytecode chunk.
    /// </summary>
    public SqFiber Spawn(BytecodeChunk chunk, string name = "script",
        Dictionary<string, SqValue>? globals = null)
    {
        var vm = new SqVm(chunk, globals ?? new Dictionary<string, SqValue>(StringComparer.OrdinalIgnoreCase), this);
        var fiber = new SqFiber(name, vm, this, CurrentTime);
        _readyQueue.Enqueue(fiber);
        TotalFibersCreated++;
        return fiber;
    }

    object ISqScheduler.Spawn(BytecodeChunk chunk, string name, SqValue[]? args)
    {
        var globals = new Dictionary<string, SqValue>(StringComparer.OrdinalIgnoreCase);
        var vm = new SqVm(chunk, globals, this);
        // Inject parent's commands
        if (NularCommands != null)
            foreach (var kv in NularCommands) vm.RegisterNular(kv.Key, kv.Value);
        if (UnaryCommands != null)
            foreach (var kv in UnaryCommands) vm.RegisterUnary(kv.Key, kv.Value);
        if (BinaryCommands != null)
            foreach (var kv in BinaryCommands) vm.RegisterBinary(kv.Key, kv.Value);
        if (args != null && args.Length > 0) vm.SetLocal(0, args[0]);
        var fiber = new SqFiber(name, vm, this, CurrentTime);
        _readyQueue.Enqueue(fiber);
        TotalFibersCreated++;
        return fiber.Handle;
    }

    void ISqScheduler.SleepCurrent(double seconds)
    {
        if (_activeFiber != null) { _activeFiber.Sleep(seconds); _waitingFibers.Add(_activeFiber); }
    }

    void ISqScheduler.YieldCurrent()
    {
        if (_activeFiber != null) { _activeFiber.State = FiberState.Ready; _readyQueue.Enqueue(_activeFiber); }
    }

    /// <summary>
    /// Execute one tick of the scheduler. Runs fibers until time budget is exhausted
    /// or no more ready fibers exist.
    /// </summary>
    public void Tick()
    {
        if (IsRunning) return; // Prevent re-entry
        IsRunning = true;

        double elapsedMs = 0;
        _stopwatch.Restart();

        // Wake up fibers whose wait time has passed
        WakeWaitingFibers();
        // Process expired timeouts
        ProcessTimeouts();
        _stopwatch.Restart(); // don't count wake-up time

        while (_readyQueue.Count > 0 && elapsedMs < TimeBudgetMs)
        {
            var fiber = _readyQueue.Dequeue();
            _activeFiber = fiber;
            fiber.State = FiberState.Running;

            try
            {
                var vmState = fiber.Vm.ExecuteStep();
                if (vmState == VmState.Completed)
                {
                    fiber.Result = fiber.Vm.Result;
                    fiber.State = FiberState.Completed;
                    fiber.Handle.Resolve(fiber.Vm.Result);
                    _completedFibers.Add(fiber);
                }
                else if (vmState == VmState.Yielded && fiber.WaitUntil > 0)
                {
                    // Already added to waiting by SleepCurrent — do nothing
                }
                else
                {
                    fiber.State = FiberState.Ready;
                    _readyQueue.Enqueue(fiber);
                }
            }
            catch (Exception ex)
            {
                fiber.State = FiberState.Terminated;
                Console.Error.WriteLine($"[Scheduler:{_name}] Fiber #{fiber.Id} error: {ex.Message}");
            }

            _activeFiber = null;
            elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
        }

        IsRunning = false;
    }

    /// <summary>
    /// Put the current fiber to sleep for a duration.
    /// </summary>
    public void SleepCurrent(double seconds)
    {
        if (_activeFiber != null)
        {
            _activeFiber.Sleep(seconds);
            _waitingFibers.Add(_activeFiber);
        }
    }

    /// <summary>
    /// Yield the current fiber back to the ready queue.
    /// </summary>
    public void YieldCurrent()
    {
        if (_activeFiber != null)
        {
            _activeFiber.State = FiberState.Ready;
            _readyQueue.Enqueue(_activeFiber);
        }
    }

    object ISqScheduler.SpawnOn(string schedulerName, BytecodeChunk chunk, string name, SqValue[]? args)
    {
        var target = Find(schedulerName);
        if (target == null)
            throw new InvalidOperationException($"Scheduler '{schedulerName}' not found. Available: {string.Join(", ", _registry.Keys)}");

        var globals = new Dictionary<string, SqValue>(StringComparer.OrdinalIgnoreCase);
        var vm = new SqVm(chunk, globals, target);
        if (target.NularCommands != null)
            foreach (var kv in target.NularCommands) vm.RegisterNular(kv.Key, kv.Value);
        if (target.UnaryCommands != null)
            foreach (var kv in target.UnaryCommands) vm.RegisterUnary(kv.Key, kv.Value);
        if (target.BinaryCommands != null)
            foreach (var kv in target.BinaryCommands) vm.RegisterBinary(kv.Key, kv.Value);
        if (args != null && args.Length > 0) vm.SetLocal(0, args[0]);
        var fiber = new SqFiber(name, vm, target, target.CurrentTime);
        target._readyQueue.Enqueue(fiber);
        target.TotalFibersCreated++;
        return fiber.Handle;
    }

    void ISqScheduler.AwaitHandle(object handleObj, double timeoutSeconds)
    {
        if (_activeFiber == null) return;
        var handle = (ScriptHandle)handleObj;

        if (handle.IsResolved)
        {
            // Already resolved — push value and continue
            _activeFiber.State = FiberState.Ready;
            _readyQueue.Enqueue(_activeFiber);
            return;
        }

        // Mark fiber as waiting on this handle
        _activeFiber.State = FiberState.Waiting;
        _activeFiber.WaitUntil = timeoutSeconds < double.PositiveInfinity
            ? CurrentTime + timeoutSeconds
            : double.PositiveInfinity;

        _waitingFibers.Add(_activeFiber);

        // When handle resolves, wake the fiber
        handle.OnResolved += _ =>
        {
            if (_activeFiber != null && _activeFiber.State == FiberState.Waiting)
            {
                _activeFiber.State = FiberState.Ready;
                _readyQueue.Enqueue(_activeFiber);
                _waitingFibers.Remove(_activeFiber);
            }
        };
    }

    void ISqScheduler.TerminateHandle(object handleObj, SqValue? result)
    {
        var handle = (ScriptHandle)handleObj;
        handle.Resolve(result);
    }

    bool ISqScheduler.IsHandleResolved(object handleObj)
    {
        return ((ScriptHandle)handleObj).IsResolved;
    }

    object ISqScheduler.ScheduleTimeout(object handleObj, double timeoutSeconds)
    {
        var source = (ScriptHandle)handleObj;

        // If source already resolved, return it directly
        if (source.IsResolved)
            return source;

        // Create new standalone handle that races source vs timer
        var timeoutHandle = new ScriptHandle();
        bool done = false;

        // When source resolves first, resolve timeout handle with source value
        source.OnResolved += val =>
        {
            if (!done)
            {
                done = true;
                timeoutHandle.Resolve(val);
            }
        };

        // Track for timer expiry
        _pendingTimeouts.Add((timeoutHandle, CurrentTime + timeoutSeconds, source));

        return timeoutHandle;
    }

    void ISqScheduler.Print(string message) => Console.WriteLine(message);

    // --- Timeout processing ---

    private void ProcessTimeouts()
    {
        if (_pendingTimeouts.Count == 0) return;
        double now = CurrentTime;

        for (int i = _pendingTimeouts.Count - 1; i >= 0; i--)
        {
            var (handle, expiry, source) = _pendingTimeouts[i];
            if (now >= expiry)
            {
                // Timer expired — resolve with nil if not already resolved
                if (!handle.IsResolved)
                {
                    handle.Resolve(SqValue.Nil);
                }
                _pendingTimeouts.RemoveAt(i);
            }
        }
    }

    // --- Internal ---
    private void WakeWaitingFibers()
    {
        double now = CurrentTime;
        for (int i = _waitingFibers.Count - 1; i >= 0; i--)
        {
            var fiber = _waitingFibers[i];
            if (fiber.WaitUntil <= now)
            {
                fiber.State = FiberState.Ready;
                _readyQueue.Enqueue(fiber);
                _waitingFibers.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Execute code directly, bypassing the fiber queue (unscheduled execution).
    /// The code runs synchronously in the caller's thread, not in a fiber.
    /// Useful for isNil { code } equivalent — callUnscheduled { code }.
    /// </summary>
    SqValue ISqScheduler.CallUnscheduled(BytecodeChunk chunk, SqValue[]? args)
    {
        var vm = new SqVm(chunk, null, null);
        if (args != null && args.Length > 0) vm.SetLocal(0, new SqValue(SqType.Array, new SqArray(args, ownerSchedulerId: _schedulerId)));
        return vm.Execute();
    }
}

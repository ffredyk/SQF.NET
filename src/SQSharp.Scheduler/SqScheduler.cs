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
    private readonly string _name;
    private readonly int _schedulerId;
    private readonly Queue<SqFiber> _readyQueue = new();
    private readonly List<SqFiber> _waitingFibers = new();
    private readonly List<SqFiber> _completedFibers = new();
    private SqFiber? _activeFiber;
    private readonly Stopwatch _stopwatch = new();

    // Command registries (injected by host for spawned scripts)
    public Dictionary<string, Func<SqValue>>? NularCommands { get; set; }
    public Dictionary<string, Func<SqValue, SqValue>>? UnaryCommands { get; set; }
    public Dictionary<string, Func<SqValue, SqValue, SqValue>>? BinaryCommands { get; set; }

    int ISqScheduler.SchedulerId => _schedulerId;

    /// <summary>Public scheduler ID (matches ISqScheduler.SchedulerId).</summary>
    public int SchedulerId => _schedulerId;

    /// <summary>Maximum milliseconds to run fibers per tick.</summary>
    public double TimeBudgetMs { get; set; } = 3.0;

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
    }

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

    void ISqScheduler.Print(string message) => Console.WriteLine(message);

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

    /// <summary>
    /// Terminate a fiber by its handle.
    /// </summary>
    public void Terminate(SqFiber fiber, SqValue? result = null)
    {
        fiber.State = FiberState.Terminated;
        fiber.Handle.Resolve(result);
        _completedFibers.Add(fiber);
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
}

using System;
using System.Collections.Generic;
using SQSharp.Core;
using OpCode = SQSharp.Core.OpCode;

namespace SQSharp.VM;

/// <summary>
/// Result of a VM execution step.
/// </summary>
public enum VmState
{
    Running,
    Yielded,     // Fiber yielded (sleep/waitUntil/await)
    Completed,   // Fiber returned
    Error,       // Runtime error
}

/// <summary>
/// Stack-based virtual machine that executes SQ# bytecode cooperatively.
/// Each fiber has its own VM instance. The scheduler drives execution via ExecuteStep().
/// </summary>
public class SqVm
{
    private readonly BytecodeChunk _chunk;
    private readonly SqValue[] _stack;
    private int _sp;
    private int _ip;
    private VmState _state = VmState.Running;

    // Locals
    private readonly SqValue[] _locals;

    // Track which local slots are defined (nil assignment deletes)
    private readonly bool[] _localDefined;

    // Globals (shared reference from scheduler)
    private readonly Dictionary<string, SqValue> _globals;

    // Scheduler callback for spawn/sleep/yield
    private readonly ISqScheduler? _scheduler;
    private int _schedulerId;

    // Command registry (separate from chunk globals to avoid index conflicts)
    private readonly Dictionary<int, Func<SqValue>> _nularCommands = new();
    private readonly Dictionary<int, Func<SqValue, SqValue>> _unaryCommands = new();
    private readonly Dictionary<int, Func<SqValue, SqValue, SqValue>> _binaryCommands = new();
    private readonly Dictionary<string, int> _cmdNameToId = new(StringComparer.OrdinalIgnoreCase);
    private int _nextCmdId;

    // Sleep/yield tracking
    internal double YieldUntil { get; private set; }
    internal object? YieldReason { get; private set; }

    private const int MaxStack = 1024;

    // Exception handler stack (indices into instruction list)
    private readonly Stack<int> _handlerStack = new();

    public VmState State => _state;
    public SqValue? Result { get; private set; }

    public SqVm(BytecodeChunk chunk, Dictionary<string, SqValue>? globals = null,
        ISqScheduler? scheduler = null)
    {
        _chunk = chunk;
        _stack = new SqValue[MaxStack];
        _sp = 0;
        _ip = 0;
        _locals = new SqValue[chunk.LocalCount];
        _localDefined = new bool[chunk.LocalCount];
        // Slot 0 is _this — always defined (set by call/spawn, default nil for bare VM)
        if (chunk.LocalCount > 0)
            _localDefined[0] = true;
        _globals = globals ?? new Dictionary<string, SqValue>(StringComparer.OrdinalIgnoreCase);
        _scheduler = scheduler;
        _schedulerId = scheduler?.SchedulerId ?? 0;
        RegisterBuiltinCommands();
    }

    /// <summary>
    /// Execute the script to completion (synchronous, for simple scripts / REPL).
    /// </summary>
    public SqValue Execute()
    {
        while (_state == VmState.Running)
            ExecuteStep();
        return Result ?? SqValue.Nil;
    }

    /// <summary>
    /// Execute one instruction (or until yield). Returns new state.
    /// </summary>
    public VmState ExecuteStep()
    {
        if (_state != VmState.Running) return _state;
        if (_ip >= _chunk.Instructions.Count)
        {
            _state = VmState.Completed;
            Result = _sp > 0 ? _stack[_sp - 1] : SqValue.Nil;
            return _state;
        }

        var inst = _chunk.Instructions[_ip];
        _ip++; // advance IP BEFORE execution (so yield/error knows where to resume)

        switch (inst.OpCode)
            {
                case OpCode.PushConst:
                    Push(_chunk.Constants[inst.Operand]);
                    break;

                case OpCode.PushLocal:
                    {
                        int slot = inst.Operand;
                        if (!_localDefined[slot])
                            throw new SqUndefinedVariableError($"local slot {slot}");
                        Push(_locals[slot]);
                    }
                    break;

                case OpCode.StoreLocal:
                    {
                        var val = Pop();
                        int slot = inst.Operand;
                        if (val.IsNil)
                        {
                            // nil assignment deletes the variable (SQF behavior)
                            _localDefined[slot] = false;
                            _locals[slot] = SqValue.Nil;
                        }
                        else
                        {
                            _localDefined[slot] = true;
                            _locals[slot] = val;
                        }
                    }
                    break;

                case OpCode.PushGlobal:
                    {
                        string name = _chunk.GlobalNames[inst.Operand];
                        // Check global variables first, then nular commands
                        if (_globals.TryGetValue(name, out var globalVal))
                        {
                            Push(globalVal);
                        }
                        else
                        {
                            int cmdId = ResolveCommandId(inst.Operand);
                            if (cmdId >= 0 && _nularCommands.TryGetValue(cmdId, out var nular))
                                Push(nular());
                            else
                                throw new SqUndefinedVariableError(name);
                        }
                    }
                    break;

                case OpCode.StoreGlobal:
                    {
                        string name = _chunk.GlobalNames[inst.Operand];
                        var val = Pop();
                        if (val.IsNil)
                        {
                            // nil assignment deletes the global variable (SQF behavior)
                            _globals.Remove(name);
                        }
                        else
                        {
                            _globals[name] = val;
                        }
                    }
                    break;

                case OpCode.NularCall:
                    {
                        int cmdId = ResolveCommandId(inst.Operand);
                        if (cmdId >= 0 && _nularCommands.TryGetValue(cmdId, out var nular))
                            Push(nular());
                        else
                            Push(SqValue.Nil);
                    }
                    break;

                case OpCode.UnaryCall:
                    {
                        var arg = Pop();
                        int cmdId = ResolveCommandId(inst.Operand);
                        if (cmdId >= 0 && _unaryCommands.TryGetValue(cmdId, out var unary))
                        {
                            try { Push(unary(arg)); }
                            catch (Exception ex) { Push(MakeError($"{ex.GetType().Name}: {ex.Message}")); }
                        }
                        else
                            Push(SqValue.Nil);
                    }
                    break;

                case OpCode.BinaryCall:
                    {
                        var right = Pop();
                        var left = Pop();
                        int cmdId = ResolveCommandId(inst.Operand);
                        if (cmdId >= 0 && _binaryCommands.TryGetValue(cmdId, out var binary))
                        {
                            try { Push(binary(left, right)); }
                            catch (Exception ex) { Push(MakeError($"{ex.GetType().Name}: {ex.Message}")); }
                        }
                        else
                            Push(SqValue.Nil);
                    }
                    break;

                case OpCode.MakeArray:
                    {
                        var arr = new SqArray(ownerSchedulerId: _schedulerId);
                        int count = inst.Operand;
                        // Items are on stack in order: first pushed = first element
                        int startIdx = _sp - count;
                        for (int i = 0; i < count; i++)
                            arr.PushBack(_stack[startIdx + i]);
                        _sp = startIdx;
                        Push(new SqValue(Core.SqType.Array, arr));
                    }
                    break;

                case OpCode.MakeHashMap:
                    Push(new SqValue(Core.SqType.HashMap, new SqHashMap(ownerSchedulerId: _schedulerId)));
                    break;

                case OpCode.MakeCode:
                    {
                        var childChunk = _chunk.Children[inst.Operand];
                        var code = new SqCode(null, bytecode: null, bytecodeOffset: inst.Operand);
                        Push(new SqValue(Core.SqType.Code, code));
                    }
                    break;

                case OpCode.MakeShared:
                    {
                        var val = Pop();
                        SqSharedValue shared;
                        if (val.IsNumber)
                            shared = new SqSharedValue(val.AsNumber());
                        else if (val.IsBool)
                            shared = new SqSharedValue(val.AsBool());
                        else
                            shared = new SqSharedValue(0.0); // default for nil
                        Push(new SqValue(Core.SqType.Shared, shared));
                    }
                    break;

                case OpCode.IsNilLocal:
                    {
                        int slot = inst.Operand;
                        Push(new SqValue(!_localDefined[slot]));
                    }
                    break;

                case OpCode.IsNilGlobal:
                    {
                        string name = _chunk.GlobalNames[inst.Operand];
                        int cmdId = ResolveCommandId(inst.Operand);
                        bool exists = _globals.ContainsKey(name)
                            || (cmdId >= 0 && _nularCommands.ContainsKey(cmdId));
                        Push(new SqValue(!exists));
                    }
                    break;

                case OpCode.Jump:
                    _ip = inst.Operand;
                    break;

                case OpCode.JumpIfFalse:
                    if (!IsTruthy(Pop()))
                        _ip = inst.Operand;
                    break;

                case OpCode.JumpIfTrue:
                    if (IsTruthy(Pop()))
                        _ip = inst.Operand;
                    break;

                case OpCode.Call:
                    {
                        int argCount = inst.Operand;
                        var codeVal = Pop();
                        if (codeVal.Type != SqType.Code)
                            throw new SqTypeError($"Expected Code, got {codeVal.Type}");

                        var sqCode = (SqCode)codeVal.RawObject!;
                        SqValue? arg = null;
                        if (argCount > 0) arg = Pop();

                        BytecodeChunk codeChunk;
                        if (sqCode.CompiledChunk != null)
                        {
                            // Runtime-compiled code (via compile command)
                            codeChunk = sqCode.CompiledChunk;
                        }
                        else
                        {
                            // Pre-compiled code block (from { ... } syntax)
                            int idx = sqCode.BytecodeOffset;
                            if ((uint)idx >= (uint)_chunk.Children.Count)
                                throw new SqTypeError($"Code block index {idx} out of range (children={_chunk.Children.Count})");
                            codeChunk = _chunk.Children[idx];
                        }

                        var nestedVm = new SqVm(codeChunk, _globals, _scheduler);
                        if (arg.HasValue) nestedVm._locals[0] = arg.Value;
                        var result = nestedVm.Execute();
                        if (nestedVm.State == VmState.Error)
                            throw new SqTypeError($"Code block error: {nestedVm.Result}");
                        Push(result);
                    }
                    break;

                case OpCode.Spawn:
                    {
                        var codeVal = Pop();
                        int argCount = inst.Operand;
                        SqValue? arg = null;
                        if (argCount > 0) arg = Pop();

                        if (_scheduler != null)
                        {
                            var childChunk = _chunk.Children[((SqCode)codeVal.RawObject!).BytecodeOffset];
                            var args = arg.HasValue ? new[] { arg.Value } : null;
                            var handle = _scheduler.Spawn(childChunk, "spawn", args);
                            Push(new SqValue(SqType.ScriptHandle, handle));
                        }
                        else
                        {
                            Push(SqValue.Nil); // no scheduler available
                        }
                    }
                    break;

                case OpCode.Ret:
                    _state = VmState.Completed;
                    Result = _sp > 0 ? _stack[_sp - 1] : SqValue.Nil;
                    return _state;

                case OpCode.Yield:
                    _state = VmState.Yielded;
                    YieldReason = "yield";
                    if (_scheduler != null) _scheduler.YieldCurrent();
                    return _state;

                case OpCode.Dup:
                    Push(Peek());
                    break;

                case OpCode.Pop:
                    Pop();
                    break;

                case OpCode.Swap:
                    {
                        var a = Pop();
                        var b = Pop();
                        Push(a);
                        Push(b);
                    }
                    break;

                case OpCode.TryBegin:
                    // Operand = IP of catch handler. Push onto handler stack.
                    _handlerStack.Push(inst.Operand);
                    break;

                case OpCode.TryEnd:
                    // Pop handler from stack (normal exit — no error).
                    if (_handlerStack.Count > 0)
                        _handlerStack.Pop();
                    break;

                case OpCode.Throw:
                    // Pop error value, jump to innermost catch handler.
                    var err = Pop();
                    if (_handlerStack.Count > 0)
                    {
                        int handlerIp = _handlerStack.Pop();
                        Push(err);          // push error for catch block
                        _ip = handlerIp;    // jump to handler
                    }
                    else
                    {
                        // Unhandled — terminate fiber with error
                        _state = VmState.Error;
                        Result = MakeError($"Unhandled error: {err}");
                    }
                    break;

                default:
                    throw new NotImplementedException($"OpCode {inst.OpCode} not implemented");
            }

        return _state;
    }

    // --- Stack helpers ---
    private void Push(SqValue value)
    {
        if (_sp >= MaxStack)
            throw new InvalidOperationException("Stack overflow");
        _stack[_sp++] = value;
    }

    private SqValue Pop()
    {
        if (_sp <= 0)
            throw new InvalidOperationException("Stack underflow");
        return _stack[--_sp];
    }

    private SqValue Peek() => _sp > 0 ? _stack[_sp - 1] : SqValue.Nil;

    // --- Truthiness ---
    private static bool IsTruthy(SqValue value)
    {
        return value.Type switch
        {
            Core.SqType.Nothing => false,
            Core.SqType.Boolean => value.AsBoolOrDefault(),
            Core.SqType.Number => value.AsNumberOrDefault() != 0,
            _ => true
        };
    }

    /// <summary>Extract number from value, auto-unwrapping Shared.</summary>
    private static double UnwrapNumber(SqValue value)
    {
        if (value.Type == Core.SqType.Shared)
            return ((SqSharedValue)value.RawObject!).Get().AsNumberOrDefault();
        return value.AsNumberOrDefault();
    }

    // --- Builtin commands ---
    private void RegisterBuiltinCommands()
    {
        // Arithmetic — auto-unwrap Shared values
        RegisterBinary("+", (a, b) => new SqValue(UnwrapNumber(a) + UnwrapNumber(b)));
        RegisterBinary("-", (a, b) => new SqValue(UnwrapNumber(a) - UnwrapNumber(b)));
        RegisterBinary("*", (a, b) => new SqValue(UnwrapNumber(a) * UnwrapNumber(b)));
        RegisterBinary("/", (a, b) =>
        {
            double divisor = UnwrapNumber(b);
            if (divisor == 0) throw new SqTypeError("Zero divisor");
            return new SqValue(UnwrapNumber(a) / divisor);
        });
        RegisterBinary("%", (a, b) =>
        {
            double divisor = UnwrapNumber(b);
            if (divisor == 0) throw new SqTypeError("Zero divisor");
            return new SqValue(UnwrapNumber(a) % divisor);
        });

        // Comparison — auto-unwrap Shared values
        RegisterBinary("==", (a, b) =>
        {
            if (a.Type == Core.SqType.Shared || b.Type == Core.SqType.Shared)
                return new SqValue(UnwrapNumber(a) == UnwrapNumber(b));
            return new SqValue(a.Equals(b));
        });
        RegisterBinary("!=", (a, b) =>
        {
            if (a.Type == Core.SqType.Shared || b.Type == Core.SqType.Shared)
                return new SqValue(UnwrapNumber(a) != UnwrapNumber(b));
            return new SqValue(!a.Equals(b));
        });
        RegisterBinary("<", (a, b) => new SqValue(UnwrapNumber(a) < UnwrapNumber(b)));
        RegisterBinary(">", (a, b) => new SqValue(UnwrapNumber(a) > UnwrapNumber(b)));
        RegisterBinary("<=", (a, b) => new SqValue(UnwrapNumber(a) <= UnwrapNumber(b)));
        RegisterBinary(">=", (a, b) => new SqValue(UnwrapNumber(a) >= UnwrapNumber(b)));

        // Logical
        RegisterUnary("!", a => new SqValue(!IsTruthy(a)));

        // Array operations
        RegisterBinary("pushBack", (arr, val) =>
        {
            var a = arr.AsArray();
            a.PushBack(val);
            return new SqValue((double)a.Count - 1); // returns index
        });
        RegisterBinary("select", (container, idx) =>
        {
            int i = (int)idx.AsNumberOrDefault();
            if (container.Type == Core.SqType.Array)
            {
                var a = container.AsArray();
                return i >= 0 && i < a.Count ? a[i] : SqValue.Nil;
            }
            if (container.Type == Core.SqType.String)
            {
                var s = container.AsString();
                return i >= 0 && i < s.Length ? new SqValue(s[i].ToString()) : SqValue.Nil;
            }
            return SqValue.Nil;
        });
        // Sleep / scheduling
        RegisterUnary("sleep", arg =>
        {
            double sec = arg.AsNumberOrDefault();
            if (_scheduler != null) _scheduler.SleepCurrent(sec);
            _state = VmState.Yielded;
            YieldUntil = (_scheduler?.CurrentTime ?? 0) + sec;
            YieldReason = "sleep";
            return SqValue.Nil;
        });

        RegisterUnary("count", arg =>
        {
            if (arg.Type == Core.SqType.Array)
                return new SqValue((double)arg.AsArray().Count);
            if (arg.Type == Core.SqType.String)
                return new SqValue((double)arg.AsString().Length);
            return new SqValue(0.0);
        });

        // Shared (atomic) operations
        RegisterBinary("add", (a, b) =>
        {
            if (a.Type == Core.SqType.Shared)
                return ((SqSharedValue)a.RawObject!).Add(b.AsNumberOrDefault());
            return new SqValue(a.AsNumberOrDefault() + b.AsNumberOrDefault());
        });
        RegisterBinary("sub", (a, b) =>
        {
            if (a.Type == Core.SqType.Shared)
                return ((SqSharedValue)a.RawObject!).Sub(b.AsNumberOrDefault());
            return new SqValue(a.AsNumberOrDefault() - b.AsNumberOrDefault());
        });
        RegisterUnary("get", a =>
        {
            if (a.Type == Core.SqType.Shared)
                return ((SqSharedValue)a.RawObject!).Get();
            return a; // identity for non-shared
        });
        RegisterBinary("set", (a, b) =>
        {
            if (a.Type == Core.SqType.Shared)
                return ((SqSharedValue)a.RawObject!).Set(b);
            throw new SqTypeError("'set' requires a shared variable");
        });
        RegisterBinary("compareSwap", (a, b) =>
        {
            if (a.Type != Core.SqType.Shared)
                throw new SqTypeError("'compareSwap' requires a shared variable");
            if (b.Type != Core.SqType.Array)
                throw new SqTypeError("compareSwap expects [expected, newValue] array");
            var args = b.AsArray();
            if (args.Count != 2)
                throw new SqTypeError("compareSwap expects exactly [expected, newValue]");
            return ((SqSharedValue)a.RawObject!).CompareSwap(args[0], args[1]);
        });

        // Scheduler identity (VM-level — has access to _schedulerId)
        RegisterNular("clientOwner", () => new SqValue((double)_schedulerId));
        RegisterNular("currentScheduler", () => new SqValue((double)_schedulerId));

        RegisterUnary("scheduler", arg =>
        {
            // Returns scheduler ID that owns the argument.
            // -1 if argument is not owned (not an array/hashmap, or no ownership tracking).
            if (arg.Type == Core.SqType.Array)
                return new SqValue((double)arg.AsArray().OwnerSchedulerId);
            if (arg.Type == Core.SqType.FrozenArray)
                return new SqValue(-1.0); // frozen = no owner (immutable, shared)
            if (arg.Type == Core.SqType.HashMap)
                return new SqValue((double)((SqHashMap)arg.RawObject!).OwnerSchedulerId);
            return new SqValue(-1.0);
        });

        RegisterUnary("isSchedulerLocal", arg =>
        {
            // True if argument belongs to the current scheduler.
            int owner = -1;
            if (arg.Type == Core.SqType.Array)
                owner = arg.AsArray().OwnerSchedulerId;
            else if (arg.Type == Core.SqType.HashMap)
                owner = ((SqHashMap)arg.RawObject!).OwnerSchedulerId;
            else if (arg.Type == Core.SqType.FrozenArray)
                return SqValue.True; // frozen = readable everywhere
            return new SqValue(owner == _schedulerId);
        });

        RegisterUnary("canSuspend", _ =>
        {
            // True if current fiber runs in scheduled environment.
            // In SQ#, all spawned fibers can suspend. Direct `call` from host cannot.
            return new SqValue(_state != VmState.Running || _scheduler != null);
        });

        // Error handling
        RegisterUnary("throw", err =>
        {
            if (_handlerStack.Count > 0)
            {
                int handlerIp = _handlerStack.Pop();
                Push(err);
                _ip = handlerIp;
            }
            else
            {
                _state = VmState.Error;
                Result = MakeError($"Unhandled throw: {err}");
            }
            return SqValue.Nil;
        });

        // params — handled by compiler (emits StoreLocal for each param)
        // Runtime fallback: no-op (compiler intercepts and inlines)

        // Type checks (also used by compiler-emitted code for params defaults)
        RegisterUnary("isNil", arg =>
        {
            // If arg is a string, look up global variable by name (SQF compat)
            if (arg.IsString)
            {
                string varName = arg.AsString();
                // Check if a global with this name exists
                if (_globals.TryGetValue(varName, out _))
                    return SqValue.False; // variable exists, not nil
                // Also check if it's a registered nular command
                if (_cmdNameToId.TryGetValue(varName, out _))
                    return SqValue.False; // command exists, not nil
                return SqValue.True; // undefined
            }
            // Otherwise check if the value itself is nil
            return new SqValue(arg.IsNil);
        });

        // spawnOn — spawn code on a named scheduler
        // Unary: spawnOn ["SchedulerName", {code}]
        // Binary: _args spawnOn ["SchedulerName", {code}]
        RegisterUnary("spawnOn", arg =>
        {
            var arr = arg.AsArray();
            if (arr.Count < 2) throw new SqTypeError("spawnOn requires [schedulerName, code]");
            string schedName = arr[0].AsString();
            var codeVal = arr[1];
            if (codeVal.Type != SqType.Code || _scheduler == null)
                return SqValue.Nil;
            var childChunk = _chunk.Children[((SqCode)codeVal.RawObject!).BytecodeOffset];
            var handle = _scheduler.SpawnOn(schedName, childChunk, "spawnOn", null);
            return new SqValue(SqType.ScriptHandle, handle);
        });

        RegisterBinary("spawnOn", (left, right) =>
        {
            var arr = right.AsArray();
            if (arr.Count < 2) throw new SqTypeError("spawnOn requires [schedulerName, code]");
            string schedName = arr[0].AsString();
            var codeVal = arr[1];
            if (codeVal.Type != SqType.Code || _scheduler == null)
                return SqValue.Nil;
            var childChunk = _chunk.Children[((SqCode)codeVal.RawObject!).BytecodeOffset];
            var handle = _scheduler.SpawnOn(schedName, childChunk, "spawnOn", new[] { left });
            return new SqValue(SqType.ScriptHandle, handle);
        });

        // await — suspend fiber until handle resolves
        // Unary: await _handle
        RegisterUnary("await", arg =>
        {
            if (_scheduler == null || arg.RawObject == null)
                return SqValue.Nil;
            _scheduler.AwaitHandle(arg.RawObject, double.PositiveInfinity);
            _state = VmState.Yielded;
            YieldReason = arg.RawObject;
            return SqValue.Nil;
        });

        // timeout — race handle against timer, return new handle
        // Binary: _handle timeout 5
        RegisterBinary("timeout", (handle, seconds) =>
        {
            if (_scheduler == null || handle.RawObject == null)
                return SqValue.Nil;
            double sec = seconds.AsNumberOrDefault();
            var newHandle = _scheduler.ScheduleTimeout(handle.RawObject, sec);
            return new SqValue(SqType.ScriptHandle, newHandle);
        });

        // Fiber termination
        RegisterUnary("terminate", arg =>
        {
            if (_scheduler != null && arg.RawObject != null)
            {
                _scheduler.TerminateHandle(arg.RawObject, SqValue.Nil);
            }
            return SqValue.Nil;
        });

        // scriptDone — check if handle/promise is resolved
        RegisterUnary("scriptDone", arg =>
        {
            if (arg.RawObject != null && _scheduler != null)
                return new SqValue(_scheduler.IsHandleResolved(arg.RawObject));
            return SqValue.True; // non-handle = "done"
        });
    }

    // --- Command registration ---
    private int CmdId(string name)
    {
        if (!_cmdNameToId.TryGetValue(name, out int id))
        {
            id = _nextCmdId++;
            _cmdNameToId[name] = id;
        }
        return id;
    }

    public void RegisterNular(string name, Func<SqValue> handler) => _nularCommands[CmdId(name)] = handler;
    public void RegisterUnary(string name, Func<SqValue, SqValue> handler) => _unaryCommands[CmdId(name)] = handler;
    public void RegisterBinary(string name, Func<SqValue, SqValue, SqValue> handler) => _binaryCommands[CmdId(name)] = handler;

    /// <summary>Set a local variable by slot index.</summary>
    /// <summary>
    /// Resolve a compiler-assigned global name index to a VM command ID.
    /// </summary>
    private int ResolveCommandId(int globalNameIndex)
    {
        if ((uint)globalNameIndex < (uint)_chunk.GlobalNames.Count)
        {
            string name = _chunk.GlobalNames[globalNameIndex];
            if (_cmdNameToId.TryGetValue(name, out int cmdId))
                return cmdId;
        }
        return -1;
    }

    public void SetLocal(int slot, SqValue value)
    {
        if ((uint)slot < (uint)_locals.Length)
        {
            _locals[slot] = value;
            _localDefined[slot] = true;
        }
    }

    /// <summary>Create an error value with source location.</summary>
    private SqValue MakeError(string message)
    {
        var (line, col) = _chunk.GetDebugInfo(_ip > 0 ? _ip - 1 : 0);
        string loc = _chunk.SourceFile ?? "<script>";
        return new SqValue(Core.SqType.Error, new SqError(message, loc, line, col));
    }

    /// <summary>Get current source location string for error reporting.</summary>
    private string ErrorLocation()
    {
        var (line, col) = _chunk.GetDebugInfo(_ip > 0 ? _ip - 1 : 0);
        string file = _chunk.SourceFile ?? "<script>";
        return $"{file}({line},{col})";
    }
}
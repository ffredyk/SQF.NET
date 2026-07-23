using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SQSharp.Core;
using SQSharp.Language;
using SQSharp.Compiler;
using SQSharp.Scheduler;

namespace SQSharp.Host;

/// <summary>
/// Thread safety level for host-registered commands.
/// </summary>
public enum ThreadSafety
{
    /// <summary>Only callable from the owning scheduler.</summary>
    Isolated,
    /// <summary>Safe to call from any thread (read-only commands).</summary>
    ReadOnly,
    /// <summary>Has internal locking, safe from any thread.</summary>
    Synchronized,
    /// <summary>Only callable from the main/UI scheduler.</summary>
    MainThread,
}

/// <summary>
/// Central host interface for embedding SQ# into .NET applications.
/// Registers commands, types, manages schedulers, and provides lifecycle hooks.
/// </summary>
public class SqHost
{
    private readonly Dictionary<string, Func<SqValue>> _nularCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<SqValue, SqValue>> _unaryCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (Func<SqValue, SqValue, SqValue> handler, int precedence, ThreadSafety safety)>
        _binaryCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Type> _registeredTypes = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, SqScheduler> _schedulers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SqValue> _globals = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Random _rng = new();

    /// <summary>The main scheduler (pumped by host game loop).</summary>
    public SqScheduler MainScheduler { get; }

    /// <summary>Current scheduler time.</summary>
    public double CurrentTime => MainScheduler.CurrentTime;

    // --- Multiplayer mode ---
    /// <summary>Is this host running as a dedicated server (no UI)?</summary>
    public bool IsDedicated { get; set; }

    /// <summary>Does this host have a player interface?</summary>
    public bool HasInterface { get; set; } = true;

    /// <summary>Is this host the server?</summary>
    public bool IsServer => true; // SQ# host is always the server in its process

    // --- Events ---
    public event Action<SqFiber>? OnScriptStart;
    public event Action<SqFiber, SqValue?>? OnScriptEnd;
    public event Action<SqFiber, Exception>? OnError;
    public event Action<string>? OnPrint;

    /// <summary>
    /// Create host with core commands. Call DeclareArmaCompatCommands()
    /// separately if you want Arma-style output commands (hint, systemChat, diag_log).
    /// </summary>
    public SqHost(bool includeArmaCompat = true)
    {
        MainScheduler = CreateScheduler("Main");
        RegisterCoreCommands();
        DeclareSchedulerCommands();
        if (includeArmaCompat) DeclareArmaCompatCommands();
    }

    /// <summary>
    /// Create or get a named scheduler.
    /// </summary>
    public SqScheduler CreateScheduler(string name)
    {
        if (!_schedulers.ContainsKey(name))
            _schedulers[name] = new SqScheduler(name);
        return _schedulers[name];
    }

    /// <summary>
    /// Get a named scheduler. Returns null if not found.
    /// </summary>
    public SqScheduler? GetScheduler(string name)
    {
        return _schedulers.TryGetValue(name, out var s) ? s : null;
    }

    // --- Command Registration ---
    public void RegisterNular(string name, Func<SqValue> handler)
    {
        _nularCommands[name] = handler;
    }

    public void RegisterUnary(string name, Func<SqValue, SqValue> handler,
        ThreadSafety threadSafety = ThreadSafety.Isolated)
    {
        _unaryCommands[name] = handler;
    }

    public void RegisterBinary(string name, Func<SqValue, SqValue, SqValue> handler,
        int precedence = 4, ThreadSafety threadSafety = ThreadSafety.Isolated)
    {
        _binaryCommands[name] = (handler, precedence, threadSafety);
    }

    public void RegisterType<T>(string typeName) where T : class
    {
        _registeredTypes[typeName] = typeof(T);
    }

    // --- Script Execution ---
    /// <summary>
    /// Parse, compile, and spawn a script from source code.
    /// </summary>
    public SqFiber ExecuteString(string source, string? name = null,
        string? scheduler = null, Dictionary<string, SqValue>? args = null)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        var compiler = new SQSharp.Compiler.Compiler();
        var chunk = compiler.Compile(ast);

        var globals = new Dictionary<string, SqValue>(_globals, StringComparer.OrdinalIgnoreCase);
        if (args != null)
        {
            foreach (var kv in args)
                globals[kv.Key] = kv.Value;
        }

        var sched = scheduler != null ? GetScheduler(scheduler) ?? MainScheduler : MainScheduler;

        // Inject host commands into scheduler (for nested spawn support)
        var nularReg = new Dictionary<string, Func<SqValue>>(_nularCommands, StringComparer.OrdinalIgnoreCase);
        var unaryReg = new Dictionary<string, Func<SqValue, SqValue>>(_unaryCommands, StringComparer.OrdinalIgnoreCase);
        var binaryReg = new Dictionary<string, Func<SqValue, SqValue, SqValue>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _binaryCommands) binaryReg[kv.Key] = kv.Value.handler;
        sched.NularCommands = nularReg;
        sched.UnaryCommands = unaryReg;
        sched.BinaryCommands = binaryReg;

        var fiber = sched.Spawn(chunk, name ?? "script", globals);

        // Inject host-registered commands into the fiber's VM
        foreach (var kv in _nularCommands) fiber.Vm.RegisterNular(kv.Key, kv.Value);
        foreach (var kv in _unaryCommands) fiber.Vm.RegisterUnary(kv.Key, kv.Value);
        foreach (var kv in _binaryCommands) fiber.Vm.RegisterBinary(kv.Key, kv.Value.handler);

        OnScriptStart?.Invoke(fiber);
        fiber.Handle.OnResolved += result => OnScriptEnd?.Invoke(fiber, result);

        return fiber;
    }

    /// <summary>
    /// Execute all schedulers for one tick.
    /// </summary>
    public void Tick()
    {
        foreach (var sched in _schedulers.Values)
            sched.Tick();
    }

    /// <summary>
    /// Pump the main scheduler for one frame.
    /// </summary>
    public void TickMain() => MainScheduler.Tick();

    // --- Globals ---
    public void SetGlobal(string name, SqValue value) => _globals[name] = value;
    public SqValue GetGlobal(string name) => _globals.TryGetValue(name, out var v) ? v : SqValue.Nil;

    // --- State Save/Load (game saving) ---

    /// <summary>
    /// Serialize all globals to a stream. Host uses this for game saving.
    /// Format: [int: count][string key][SqValue value]...
    /// </summary>
    public void SaveGlobals(Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(_globals.Count);
        foreach (var kv in _globals)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(kv.Key);
            w.Write(keyBytes.Length);
            w.Write(keyBytes);
            SqBinarySerializer.WriteValue(w, kv.Value);
        }
    }

    /// <summary>
    /// Deserialize globals from a stream, merging with existing.
    /// </summary>
    public void LoadGlobals(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            int keyLen = r.ReadInt32();
            string key = Encoding.UTF8.GetString(r.ReadBytes(keyLen));
            var value = SqBinarySerializer.ReadValue(r);
            _globals[key] = value;
        }
    }

    /// <summary>
    /// Save full state (globals + scheduler list).
    /// Full fiber state serialization is planned for v0.3.
    /// </summary>
    public void SaveState(Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        // Version marker
        w.Write((byte)1);
        // Globals
        SaveGlobals(stream);
        // Scheduler count + IDs
        w.Write(_schedulers.Count);
        foreach (var kv in _schedulers)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(kv.Key);
            w.Write(nameBytes.Length);
            w.Write(nameBytes);
            w.Write(kv.Value.SchedulerId);
        }
    }

    /// <summary>
    /// Load full state from a stream. Restores globals.
    /// Full fiber restoration is planned for v0.3.
    /// </summary>
    public void LoadState(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        byte version = r.ReadByte();
        if (version != 1) throw new InvalidDataException($"Unknown save state version: {version}");
        // Globals
        LoadGlobals(stream);
        // Schedulers — skip for now (host owns scheduler lifecycle)
        int schedCount = r.ReadInt32();
        for (int i = 0; i < schedCount; i++)
        {
            int nameLen = r.ReadInt32();
            r.ReadBytes(nameLen); // skip name
            r.ReadInt32();        // skip ID
        }
    }

    // --- Command registration: Core (always available) ---
    /// <summary>
    /// Register core SQ# language commands. Always available.
    /// Covers math, comparison, logic, string, array, type, and runtime.
    /// </summary>
    public void RegisterCoreCommands()
    {
        // Arithmetic (auto-unwrap Shared values)
        RegisterBinary("+", (a, b) =>
        {
            if (a.IsString || b.IsString)
                return new SqValue((a.IsString ? a.AsString() : a.ToString()) + (b.IsString ? b.AsString() : b.ToString()));
            return new SqValue(UnwrapSharedNumber(a) + UnwrapSharedNumber(b));
        }, 6);
        RegisterBinary("-", (a, b) => new SqValue(UnwrapSharedNumber(a) - UnwrapSharedNumber(b)), 6);
        RegisterBinary("*", (a, b) => new SqValue(UnwrapSharedNumber(a) * UnwrapSharedNumber(b)), 7);
        RegisterBinary("/", (a, b) =>
        {
            double divisor = UnwrapSharedNumber(b);
            if (divisor == 0) return new SqValue(double.NaN);
            return new SqValue(UnwrapSharedNumber(a) / divisor);
        }, 7);
        RegisterBinary("%", (a, b) => new SqValue(UnwrapSharedNumber(a) % UnwrapSharedNumber(b)), 7);

        // Comparison (auto-unwrap Shared values)
        RegisterBinary("==", (a, b) =>
        {
            if (a.Type == SqType.Shared || b.Type == SqType.Shared)
                return new SqValue(UnwrapSharedNumber(a) == UnwrapSharedNumber(b));
            return new SqValue(a.Equals(b));
        }, 3, ThreadSafety.ReadOnly);
        RegisterBinary("!=", (a, b) =>
        {
            if (a.Type == SqType.Shared || b.Type == SqType.Shared)
                return new SqValue(UnwrapSharedNumber(a) != UnwrapSharedNumber(b));
            return new SqValue(!a.Equals(b));
        }, 3, ThreadSafety.ReadOnly);
        RegisterBinary("<", (a, b) => new SqValue(UnwrapSharedNumber(a) < UnwrapSharedNumber(b)), 3);
        RegisterBinary(">", (a, b) => new SqValue(UnwrapSharedNumber(a) > UnwrapSharedNumber(b)), 3);
        RegisterBinary("<=", (a, b) => new SqValue(UnwrapSharedNumber(a) <= UnwrapSharedNumber(b)), 3);
        RegisterBinary(">=", (a, b) => new SqValue(UnwrapSharedNumber(a) >= UnwrapSharedNumber(b)), 3);

        // Logical
        RegisterUnary("!", a => new SqValue(!IsTruthy(a)));
        RegisterUnary("-", a => new SqValue(-UnwrapSharedNumber(a))); // unary negation
        RegisterBinary("&&", (a, b) => new SqValue(IsTruthy(a) && IsTruthy(b)), 2);
        RegisterBinary("||", (a, b) => new SqValue(IsTruthy(a) || IsTruthy(b)), 1);

        // String concatenation handled by the unified + handler above

        // Array
        RegisterUnary("count", arg =>
        {
            if (arg.Type == SqType.Array) return new SqValue((double)arg.AsArray().Count);
            if (arg.Type == SqType.String) return new SqValue((double)arg.AsString().Length);
            return new SqValue(0.0);
        }, ThreadSafety.ReadOnly);

        RegisterBinary("select", (container, idx) =>
        {
            int i = (int)idx.AsNumberOrDefault();
            if (container.Type == SqType.Array)
            {
                var a = container.AsArray();
                return i >= 0 && i < a.Count ? a[i] : SqValue.Nil;
            }
            if (container.Type == SqType.String)
            {
                var s = container.AsString();
                return i >= 0 && i < s.Length ? new SqValue(s[i].ToString()) : SqValue.Nil;
            }
            return SqValue.Nil;
        }, 4, ThreadSafety.ReadOnly);

        RegisterBinary("pushBack", (arr, val) =>
        {
            var a = arr.AsArray();
            a.PushBack(val);
            return new SqValue((double)a.Count - 1);
        }, 4);

        // set is registered below (Shared-aware, after array commands)

        RegisterBinary("append", (arr, other) =>
        {
            arr.AsArray().Append(other.AsArray());
            return SqValue.Nil;
        }, 4);

        RegisterBinary("deleteAt", (arr, idx) =>
        {
            int i = (int)idx.AsNumberOrDefault();
            return arr.AsArray().DeleteAt(i);
        }, 4);

        RegisterBinary("deleteRange", (arr, args) =>
        {
            var a = arr.AsArray();
            var argArr = args.AsArray();
            int start = (int)argArr[0].AsNumberOrDefault();
            int count = (int)argArr[1].AsNumberOrDefault();
            a.DeleteRange(start, count);
            return SqValue.Nil;
        }, 4);

        RegisterBinary("resize", (arr, size) =>
        {
            arr.AsArray().Resize((int)size.AsNumberOrDefault());
            return SqValue.Nil;
        }, 4);

        RegisterUnary("freeze", arg =>
        {
            if (arg.Type == SqType.Array)
                return new SqValue(SqType.FrozenArray, arg.AsArray().Freeze());
            return arg;
        }, ThreadSafety.ReadOnly);

        RegisterUnary("thaw", arg =>
        {
            if (arg.Type == SqType.FrozenArray || arg.Type == SqType.Array)
                return new SqValue(SqType.Array, arg.AsArray().Thaw(0));
            return arg;
        });

        RegisterUnary("isFrozen", arg => new SqValue(arg.Type == SqType.FrozenArray ||
            (arg.Type == SqType.Array && arg.AsArray().IsFrozen)), ThreadSafety.ReadOnly);

        RegisterUnary("reverse", arg => { arg.AsArray().Reverse(); return SqValue.Nil; });

        RegisterBinary("sort", (arr, asc) =>
        {
            arr.AsArray().Sort(asc.AsBoolOrDefault(true));
            return SqValue.Nil;
        }, 4);

        RegisterBinary("find", (arr, val) =>
        {
            if (arr.Type == SqType.Array) return new SqValue((double)arr.AsArray().Find(val));
            if (arr.Type == SqType.String)
            {
                int idx = arr.AsString().IndexOf(val.AsString(), StringComparison.Ordinal);
                return new SqValue((double)idx);
            }
            return new SqValue(-1.0);
        }, 4, ThreadSafety.ReadOnly);

        RegisterBinary("in", (a, b) =>
        {
            if (b.Type == SqType.Array) return new SqValue(b.AsArray().Find(a) >= 0);
            if (b.Type == SqType.String && a.Type == SqType.String)
                return new SqValue(b.AsString().Contains(a.AsString(), StringComparison.Ordinal));
            return SqValue.False;
        }, 4, ThreadSafety.ReadOnly);

        // String operations
        RegisterUnary("parseNumber", arg =>
        {
            if (double.TryParse(arg.AsString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
                return new SqValue(v);
            return SqValue.Nil;
        });

        RegisterUnary("toArray", arg =>
        {
            var s = arg.AsString();
            var arr = new SqArray();
            foreach (char c in s) arr.PushBack(new SqValue((double)c));
            return new SqValue(SqType.Array, arr);
        }, ThreadSafety.ReadOnly);

        RegisterUnary("toString", arg =>
        {
            if (arg.Type == SqType.Array)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var v in arg.AsArray()) sb.Append((char)(int)v.AsNumberOrDefault());
                return new SqValue(sb.ToString());
            }
            return new SqValue(arg.ToString() ?? "");
        }, ThreadSafety.ReadOnly);

        RegisterBinary("splitString", (str, sep) =>
        {
            var parts = str.AsString().Split(new[] { sep.AsString() }, StringSplitOptions.None);
            var arr = new SqArray();
            foreach (var p in parts) arr.PushBack(new SqValue(p));
            return new SqValue(SqType.Array, arr);
        }, 4);

        RegisterBinary("joinString", (arr, sep) =>
        {
            var strings = new List<string>();
            foreach (var v in arr.AsArray()) strings.Add(v.AsString());
            return new SqValue(string.Join(sep.AsString(), strings));
        }, 4);

        RegisterUnary("toLower", arg => new SqValue(arg.AsString().ToLowerInvariant()), ThreadSafety.ReadOnly);
        RegisterUnary("toUpper", arg => new SqValue(arg.AsString().ToUpperInvariant()), ThreadSafety.ReadOnly);
        RegisterUnary("trim", arg => new SqValue(arg.AsString().Trim()), ThreadSafety.ReadOnly);

        // Math
        RegisterUnary("abs", arg => new SqValue(Math.Abs(arg.AsNumberOrDefault())));
        RegisterUnary("floor", arg => new SqValue(Math.Floor(arg.AsNumberOrDefault())));
        RegisterUnary("ceil", arg => new SqValue(Math.Ceiling(arg.AsNumberOrDefault())));
        RegisterUnary("round", arg => new SqValue(Math.Round(arg.AsNumberOrDefault())));
        RegisterUnary("sqrt", arg => new SqValue(Math.Sqrt(Math.Max(0, arg.AsNumberOrDefault()))));
        RegisterBinary("min", (a, b) => new SqValue(Math.Min(UnwrapSharedNumber(a), UnwrapSharedNumber(b))), 6);
        RegisterBinary("max", (a, b) => new SqValue(Math.Max(UnwrapSharedNumber(a), UnwrapSharedNumber(b))), 6);

        // Type checks
        RegisterUnary("isNil", arg => new SqValue(arg.IsNil));
        RegisterUnary("str", arg => new SqValue(arg.ToString() ?? "nil"));
        RegisterUnary("format", arg =>
        {
            var arr = arg.AsArray();
            if (arr.Count == 0) return new SqValue("");
            string template = arr[0].AsString();
            var fmtArgs = new SqValue[arr.Count - 1];
            for (int i = 1; i < arr.Count; i++) fmtArgs[i - 1] = arr[i];
            return new SqValue(SqFormat.Format(template, fmtArgs));
        });

        RegisterUnary("typeName", arg => new SqValue(arg.Type.ToString().ToLowerInvariant()));

        // Math commands
        RegisterUnary("abs", arg => new SqValue(Math.Abs(UnwrapSharedNumber(arg))));
        RegisterUnary("floor", arg => new SqValue(Math.Floor(UnwrapSharedNumber(arg))));
        RegisterUnary("ceil", arg => new SqValue(Math.Ceiling(UnwrapSharedNumber(arg))));
        RegisterUnary("round", arg => new SqValue(Math.Round(UnwrapSharedNumber(arg))));
        RegisterUnary("sqrt", arg => new SqValue(Math.Sqrt(Math.Max(0, UnwrapSharedNumber(arg)))));
        RegisterUnary("sin", arg => new SqValue(Math.Sin(UnwrapSharedNumber(arg))));
        RegisterUnary("cos", arg => new SqValue(Math.Cos(UnwrapSharedNumber(arg))));
        RegisterUnary("tan", arg => new SqValue(Math.Tan(UnwrapSharedNumber(arg))));
        RegisterUnary("asin", arg => new SqValue(Math.Asin(UnwrapSharedNumber(arg))));
        RegisterUnary("acos", arg => new SqValue(Math.Acos(UnwrapSharedNumber(arg))));
        RegisterUnary("exp", arg => new SqValue(Math.Exp(UnwrapSharedNumber(arg))));
        RegisterUnary("log", arg => new SqValue(Math.Log(Math.Max(double.Epsilon, UnwrapSharedNumber(arg)))));
        RegisterBinary("atan2", (y, x) => new SqValue(Math.Atan2(UnwrapSharedNumber(y), UnwrapSharedNumber(x))), 6);
        RegisterBinary("pow", (a, b) => new SqValue(Math.Pow(UnwrapSharedNumber(a), UnwrapSharedNumber(b))), 7);
        RegisterUnary("random", arg =>
        {
            double max = UnwrapSharedNumber(arg);
            lock (_rng) return new SqValue(_rng.NextDouble() * max);
        });

        // Runtime compilation
        RegisterUnary("compile", arg =>
        {
            var source = arg.AsString();
            var lexer = new Lexer(source, legacyComments: false);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var ast = parser.Parse();
            var compiler = new SQSharp.Compiler.Compiler();
            var chunk = compiler.Compile(ast, "<compile>");
            return new SqValue(SqType.Code, new SqCode(source, chunk.LocalCount,
                compiledChunk: chunk));
        });

        RegisterNular("createHashMap", () => new SqValue(SqType.HashMap, new SqHashMap()));

        RegisterUnary("execVM", arg =>
        {
            string path = arg.AsString();
            string source = File.ReadAllText(path);
            var fiber = ExecuteString(source, path);
            return new SqValue(SqType.ScriptHandle, fiber.Handle);
        });

        // Nular
        RegisterNular("nil", () => SqValue.Nil);
        RegisterNular("true", () => SqValue.True);
        RegisterNular("false", () => SqValue.False);
        RegisterNular("diag_tickTime", () => new SqValue(CurrentTime));

        // Shared (atomic) operations
        RegisterBinary("add", (a, b) =>
        {
            if (a.Type == SqType.Shared)
                return ((SqSharedValue)a.RawObject!).Add(UnwrapSharedNumber(b));
            return new SqValue(UnwrapSharedNumber(a) + UnwrapSharedNumber(b));
        }, 4);
        RegisterBinary("sub", (a, b) =>
        {
            if (a.Type == SqType.Shared)
                return ((SqSharedValue)a.RawObject!).Sub(UnwrapSharedNumber(b));
            return new SqValue(UnwrapSharedNumber(a) - UnwrapSharedNumber(b));
        }, 4);
        RegisterUnary("get", a =>
        {
            if (a.Type == SqType.Shared)
                return ((SqSharedValue)a.RawObject!).Get();
            return a;
        });
        RegisterBinary("get", (container, key) =>
        {
            if (container.Type == SqType.Shared)
                return ((SqSharedValue)container.RawObject!).Get();
            if (container.Type == SqType.HashMap)
                return ((SqHashMap)container.RawObject!).Get(key);
            if (container.Type == SqType.Array)
            {
                int i = (int)key.AsNumberOrDefault();
                var arr = container.AsArray();
                return i >= 0 && i < arr.Count ? arr[i] : SqValue.Nil;
            }
            return container;
        }, 4);
        RegisterBinary("set", (a, b) =>
        {
            // Dispatch by type:
            if (a.Type == SqType.Shared)
                return ((SqSharedValue)a.RawObject!).Set(b);
            if (a.Type == SqType.Array)
            {
                var arr = a.AsArray();
                if (b.Type == SqType.Array)
                {
                    var argArr = b.AsArray();
                    int i = (int)argArr[0].AsNumberOrDefault();
                    arr[i] = argArr[1];
                }
                else
                {
                    // HashMap-style: set [key, value] on array → index assignment
                    int i = (int)b.AsNumberOrDefault();
                    arr[i] = b; // fallback: treat as index
                }
                return SqValue.Nil;
            }
            if (a.Type == SqType.HashMap)
            {
                if (b.Type == SqType.Array)
                {
                    var argArr = b.AsArray();
                    ((SqHashMap)a.RawObject!).Set(argArr[0], argArr[1]);
                }
                return SqValue.Nil;
            }
            throw new SqTypeError($"'set' does not support type {a.Type}");
        }, 4);
        RegisterBinary("compareSwap", (a, b) =>
        {
            if (a.Type != SqType.Shared)
                throw new SqTypeError("'compareSwap' requires a shared variable");
            if (b.Type != SqType.Array)
                throw new SqTypeError("compareSwap expects [expected, newValue] array");
            var args = b.AsArray();
            if (args.Count != 2)
                throw new SqTypeError("compareSwap expects exactly [expected, newValue]");
            return ((SqSharedValue)a.RawObject!).CompareSwap(args[0], args[1]);
        }, 4);

        // Output (generic — host defines OnPrint handler)
        RegisterUnary("print", arg => { OnPrint?.Invoke(arg.ToString() ?? "nil"); return SqValue.Nil; });
    }

    /// <summary>
    /// Register Arma-compatible output commands.
    /// </summary>
    public void DeclareArmaCompatCommands()
    {
        RegisterUnary("hint", arg => { OnPrint?.Invoke(arg.ToString() ?? "nil"); return SqValue.Nil; });
        RegisterUnary("systemChat", arg => { OnPrint?.Invoke(arg.ToString() ?? "nil"); return SqValue.Nil; });
        RegisterUnary("diag_log", arg => { OnPrint?.Invoke($"[DIAG] {arg}"); return SqValue.Nil; });
    }

    /// <summary>
    /// Register multiplayer commands (isServer, remoteExec, publicVariable, etc).
    /// Call this if your host runs in a multiplayer context.
    /// </summary>
    public void DeclareMultiplayerCommands()
    {
        // Machine identity
        RegisterNular("isServer", () => new SqValue(IsServer));
        RegisterNular("isDedicated", () => new SqValue(IsDedicated));
        RegisterNular("hasInterface", () => new SqValue(HasInterface));
        RegisterNular("isClient", () => new SqValue(!IsServer));

        // Remote execution — [params, codeString, target, isJip]
        // target: 0 = everyone, 2 = server only, -2 = everyone except server, clientId = specific client
        RegisterBinary("remoteExec", (args, cmdArray) =>
        {
            var arr = cmdArray.AsArray();
            string cmdName = arr[0].AsString();
            int target = arr.Count > 1 ? (int)arr[1].AsNumberOrDefault() : 0;
            bool isJip = arr.Count > 2 ? arr[2].AsBoolOrDefault() : false;

            // For now: execute locally if target matches
            if (target == 0 || target == 2 && IsServer || target > 2)
            {
                // Execute the command locally
                // Full implementation would serialize and send over network
                OnPrint?.Invoke($"[MP] remoteExec {cmdName} target={target} jip={isJip}");
            }
            return SqValue.Nil;
        }, 4);

        RegisterBinary("remoteExecCall", (args, cmdArray) =>
        {
            // Same as remoteExec but uses call instead of spawn
            // Full implementation in network layer
            return SqValue.Nil;
        }, 4);

        // Public variable broadcast
        RegisterUnary("publicVariable", arg =>
        {
            string varName = arg.AsString();
            // Full implementation: serialize and broadcast to all clients
            OnPrint?.Invoke($"[MP] publicVariable '{varName}'");
            return SqValue.Nil;
        });

        RegisterUnary("publicVariableServer", arg =>
        {
            string varName = arg.AsString();
            // Send to server only
            return SqValue.Nil;
        });

        RegisterBinary("publicVariableClient", (arg, clientId) =>
        {
            string varName = arg.AsString();
            int target = (int)clientId.AsNumberOrDefault();
            // Send to specific client
            return SqValue.Nil;
        }, 4);

        // Player info
        RegisterNular("player", () => new SqValue(HasInterface ? 1.0 : 0.0)); // placeholder: returns player ID
        RegisterNular("allPlayers", () =>
        {
            var arr = new SqArray();
            if (HasInterface) arr.PushBack(new SqValue(1.0));
            return new SqValue(SqType.Array, arr);
        });

        // JIP
        RegisterNular("didJIP", () => SqValue.False); // placeholder
        RegisterNular("didJIPOwner", () => SqValue.False); // placeholder

        // Network ID
        // clientOwner is a VM builtin (returns actual fiber scheduler ID)
        RegisterUnary("owner", obj => new SqValue(2.0)); // placeholder: machine owner ID (hardware-level)
        RegisterUnary("netId", obj => new SqValue("0:0")); // placeholder: returns netId string
        RegisterUnary("objectFromNetId", id => SqValue.Nil); // placeholder
    }

    /// <summary>
    /// Register scheduler introspection and management commands.
    /// Call this if your host uses multiple schedulers.
    /// </summary>
    public void DeclareSchedulerCommands()
    {
        // Scheduler listing
        RegisterNular("allSchedulers", () =>
        {
            var arr = new SqArray();
            foreach (var kv in _schedulers)
                arr.PushBack(new SqValue((double)kv.Value.SchedulerId));
            return new SqValue(SqType.Array, arr);
        });

        RegisterUnary("schedulerName", arg =>
        {
            int id = (int)arg.AsNumberOrDefault();
            foreach (var kv in _schedulers)
                if (kv.Value.SchedulerId == id)
                    return new SqValue(kv.Key);
            return new SqValue(""); // not found
        });

        RegisterUnary("schedulerExists", arg =>
        {
            int id = (int)arg.AsNumberOrDefault();
            foreach (var kv in _schedulers)
                if (kv.Value.SchedulerId == id)
                    return SqValue.True;
            return SqValue.False;
        });

        // Scheduler stats
        RegisterUnary("schedulerBudget", arg =>
        {
            int id = (int)arg.AsNumberOrDefault();
            foreach (var kv in _schedulers)
                if (kv.Value.SchedulerId == id)
                    return new SqValue(kv.Value.TimeBudgetMs);
            return new SqValue(-1.0);
        });

        RegisterBinary("setSchedulerBudget", (arg, ms) =>
        {
            int id = (int)arg.AsNumberOrDefault();
            foreach (var kv in _schedulers)
                if (kv.Value.SchedulerId == id)
                {
                    kv.Value.TimeBudgetMs = Math.Max(0.1, ms.AsNumberOrDefault());
                    return SqValue.True;
                }
            return SqValue.False;
        }, 4);

        RegisterUnary("fiberCount", arg =>
        {
            int id = (int)arg.AsNumberOrDefault();
            foreach (var kv in _schedulers)
                if (kv.Value.SchedulerId == id)
                    return new SqValue((double)(kv.Value.ReadyCount + kv.Value.WaitingCount));
            return new SqValue(-1.0);
        });

        RegisterUnary("readyFiberCount", arg =>
        {
            int id = (int)arg.AsNumberOrDefault();
            foreach (var kv in _schedulers)
                if (kv.Value.SchedulerId == id)
                    return new SqValue((double)kv.Value.ReadyCount);
            return new SqValue(-1.0);
        });

        RegisterUnary("waitingFiberCount", arg =>
        {
            int id = (int)arg.AsNumberOrDefault();
            foreach (var kv in _schedulers)
                if (kv.Value.SchedulerId == id)
                    return new SqValue((double)kv.Value.WaitingCount);
            return new SqValue(-1.0);
        });

        RegisterUnary("schedulerLoad", arg =>
        {
            int id = (int)arg.AsNumberOrDefault();
            foreach (var kv in _schedulers)
                if (kv.Value.SchedulerId == id)
                {
                    double budget = kv.Value.TimeBudgetMs;
                    if (budget <= 0) return new SqValue(0.0);
                    // Load = fibers * avg step time / budget, capped at 100%
                    double load = Math.Min(100.0,
                        (kv.Value.ReadyCount + kv.Value.WaitingCount) * 0.15 / budget * 100.0);
                    return new SqValue(load);
                }
            return new SqValue(-1.0);
        });

        // Cross-scheduler data transfer
        RegisterBinary("sendTo", (arg, targetSched) =>
        {
            int targetId = (int)targetSched.AsNumberOrDefault();
            if (arg.Type == SqType.Array)
            {
                var arr = arg.AsArray();
                // Transfer ownership: only works if caller owns the array
                // (enforced by the fact that we're on the owning scheduler)
                var transferred = new SqArray(arr, ownerSchedulerId: targetId);
                return new SqValue(SqType.Array, transferred);
            }
            throw new SqTypeError("'sendTo' requires an Array argument");
        }, 4);
    }

    /// <summary>Extract number from value, auto-unwrapping Shared.</summary>
    private static double UnwrapSharedNumber(SqValue value)
    {
        if (value.Type == SqType.Shared)
            return ((SqSharedValue)value.RawObject!).Get().AsNumberOrDefault();
        return value.AsNumberOrDefault();
    }

    private static bool IsTruthy(SqValue v) => v.Type switch
    {
        SqType.Nothing => false,
        SqType.Boolean => v.AsBoolOrDefault(),
        SqType.Number => v.AsNumberOrDefault() != 0,
        _ => true
    };
}

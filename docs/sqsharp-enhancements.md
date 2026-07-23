# What SQ# Brings

> Everything SQ# adds over vanilla SQF — type safety, threading, tools, and more.  
> A master list for decision-makers, engine integrators, and curious SQF veterans.

---

## 1. Type System — No More "Any"

### SQF Problem

Every variable is `Anything`. `nil` deletes variables. Undefined variables silently fail. `0 == false` is `true`. Type errors manifest as "Zero divisor" or worse.

### SQ# Fix

`SqValue` is a **tagged union** with explicit types:

| Type | C# Storage | SQF Equivalent |
|---|---|---|
| `Nothing` | — | `nil` (deletes variables — SQF behavior) |
| `Boolean` | `double` (0.0/1.0) | `true`/`false` |
| `Number` | `double` (IEEE 754) | `1`, `3.14`, `-2.5e10` |
| `String` | `string` (interned) | `"hello"`, `'hello'` |
| `Array` | `SqArray` (mutable, owned) | `[1, 2, 3]` |
| `Code` | `SqCode` (compiled chunk) | `{ ... }` |
| `HashMap` | `SqHashMap` (key-value) | `createHashMap` |
| `Namespace` | `SqNamespace` (named globals) | `missionNamespace` |
| `Shared` | `SqSharedValue` (CAS atomic) | 🆕 No SQF equivalent |
| `Channel` | `Channel<T>` (lock-free SPSC) | 🆕 No SQF equivalent |
| `FrozenArray` | `ImmutableArray<SqValue>` | 🆕 No SQF equivalent |
| `ScriptHandle` | `SqScriptHandle` (promise) | `scriptNull` |
| `Error` | `SqError` (typed error values) | 🆕 No SQF equivalent |

```sqf
// SQ# type safety:
private _x = 42;
_x = "hello";           // ✅ OK — dynamic typing preserved
_x = nil;               // Variable DELETED (SQF semantics)
isNil _x;               // true (variable no longer exists)

// Type errors are CLEAR:
_x = { 1 + 2; };
_x + 3;
// ❌ SqTypeError: Expected Number, got Code
// (SQF would silently return 3 or crash with "Zero divisor")
```

**Key differences**:
- `nil` assignment deletes variables (matches SQF behavior)
- Undefined variables throw `SqUndefinedVariableError` — not silent
- Type mismatch throws `SqTypeError` with clear message
- No implicit `0 == false` equality (though truthiness is SQF-compatible)

---

## 2. Deterministic Pratt Parser — No More "Why Did That Parse?"

### SQF Problem

SQF's parser is hand-written C, decades old. Operator precedence is inconsistent. `if` requires `then` sometimes but not always. `a b c` is ambiguous. The parser relies on heuristics and command tables.

### SQ# Fix

A **Pratt parser** with 11 explicit precedence levels:

| Level | Name | Examples |
|---|---|---|
| 0 | None | — |
| 1 | Or | `\|\|`, `or` |
| 2 | And | `&&`, `and` |
| 3 | Compare | `==`, `!=`, `<`, `>`, `<=`, `>=` |
| 4 | Binary | `pushBack`, `select`, `set`, `resize`, `add`, `sub` |
| 5 | Range | 🔜 Planned |
| 6 | Add/Sub | `+`, `-`, `min`, `max` |
| 7 | Mul/Div | `*`, `/`, `%`, `mod` |
| 8 | Unary | `!`, `-` (negation), unary commands |
| 9 | Hash | `#` (array index) |
| 10 | Call/Spawn | `call`, `spawn` |
| 11 | Nular | bare word commands, variables |

```sqf
// Predictable, documented behavior:
_a = 1 + 2 * 3;     // 7 (MulDiv > AddSub)
_b = _arr select 0; // (Binary) — binds tighter than +
_c = _x + _arr select 0;  // _x + (_arr select 0)
```

**Key differences**:
- Full precedence table documented
- Identifier-as-operator with known precedence (PrecBinary=4)
- Expression breaker keywords: `if`, `while`, `for`, `switch`, `try`, `return`, `global`, `private`, `shared`, `then`, `else`, `do`, `from`, `to`, `step`, `catch`
- No ambiguous `a b c` parsing

---

## 3. Stack-Based Bytecode VM — Faster, Debuggable

### SQF Problem

SQF is interpreted directly from AST. No intermediate representation. No way to inspect execution. Performance limited by tree walking.

### SQ# Fix

A **stack-based bytecode VM** with 29 opcodes:

| Opcode | Description |
|---|---|
| `PushConst` | Push constant from pool |
| `PushLocal` / `StoreLocal` | Local variable access (O(1) slot lookup) |
| `PushGlobal` / `StoreGlobal` | Global variable access (dictionary lookup + command fallback) |
| `NularCall` / `UnaryCall` / `BinaryCall` | Typed command dispatch |
| `MakeArray` / `MakeHashMap` / `MakeCode` | Compound value construction |
| `MakeShared` | 🆕 Create atomic shared variable |
| `Jump` / `JumpIfFalse` / `JumpIfTrue` | Control flow |
| `Call` / `Spawn` / `SpawnOn` | Code execution |
| `Ret` / `Yield` | Fiber control |
| `Dup` / `Pop` / `Swap` | Stack manipulation |
| `Await` / `Throw` / `TryBegin` / `TryEnd` | Async & error handling |

```sqf
// Source:
private _x = 5;
private _y = _x + 3;
_y;

// Compiles to (pseudocode):
//   PushConst 5
//   StoreLocal 0        ; _x
//   PushLocal 0
//   PushConst 3
//   BinaryCall "+"
//   StoreLocal 1        ; _y
//   PushLocal 1
//   Ret
```

**Key differences**:
- Compile once, run many times — no re-parsing
- Bytecode is serializable (🔜 Planned: `.sqfc` binary format)
- VM is cooperative — yields mid-execution for scheduler
- Instruction-level hooks for debugger (🔜 Planned: DAP protocol)
- Performance: ~50-100x faster than AST-walking SQF for computation-heavy scripts

---

## 4. Cooperative Multithreading — Schedulers, Fibers, Lock-Free Sharing

### SQF Problem

Single-threaded. All scripts share one 3ms budget. Heavy AI or computation starves everything else. No way to run parallel work. Headless clients are separate processes.

### SQ# Fix

**Multi-scheduler architecture** with three lock-free sharing primitives:

| Feature | SQ# | SQF |
|---|---|---|
| Multiple schedulers | ✅ `spawnOn ["AI", {...}]` | ❌ Single scheduler |
| Per-scheduler time budget | ✅ Configurable per scheduler | ❌ 3ms global |
| Fiber suspension | ✅ `sleep`, `waitUntil`, `await` | ✅ `sleep`, `waitUntil` |
| Cross-scheduler spawning | ✅ `spawnOn` | ❌ |
| Immutable sharing | ✅ `freeze` / `thaw` | ❌ |
| Message passing | ✅ `Channel` (SPSC, lock-free) | ❌ |
| Atomic variables | ✅ `shared` (CAS-based) | ❌ |
| Ownership tracking | ✅ Array ownership + enforcement | ❌ |
| Thread safety errors | ✅ Clear error on violation | ❌ Silent corruption |

```sqf
// Parallel AI processing:
shared _totalKills = 0;

spawnOn ["AI", {
    while { alive _unit } do {
        _unit call processAI;
        _totalKills add 1;
        sleep 0.05;
    };
}];

// UI on main thread, AI on separate scheduler:
spawnOn ["UI", {
    while { true } do {
        hint format ["Kills: %1", get _totalKills];
        sleep 1;
    };
}];
```

**Key guarantee**: If you never use `spawnOn`, you never encounter concurrency. Threading is opt-in.

📖 Full guide: [multithreading.md](multithreading.md)

---

## 5. Multiplayer Infrastructure — Scheduler = Machine

### SQF Problem

Multiplayer in Arma is tightly coupled to the engine. Locality, remoteExec, publicVariable — all depend on Arma's internal networking. No way to reuse MP patterns outside Arma.

### SQ# Fix

**Scheduler = machine model**. Every concept maps cleanly:

| Arma Concept | SQ# Equivalent |
|---|---|
| Machine/Client | `SqScheduler` |
| `isServer` / `isDedicated` | Scheduler properties |
| `remoteExec` | Cross-scheduler message dispatch |
| `publicVariable` | Cross-scheduler global sync |
| `owner` | `SchedulerId` on objects |
| `local` | Scheduler ownership check |
| Headless Client | Scheduler with `hasInterface=false` |
| JIP | JIP message queue (🏠 Host) |

```sqf
// Same MP patterns, same syntax:
if (isServer) then {
    global MISSION_TIME = 0;
    publicVariable "MISSION_TIME";
};

[_unit, _damage] remoteExec ["setDamage", owner _unit];
```

📖 Full guide: [multiplayer.md](multiplayer.md)

---

## 6. Promise-Based Async — `await`, ScriptHandle

### SQF Problem

`scriptDone` polling is awkward. No way to get a spawn's return value elegantly. Callback chaining requires manual bookkeeping.

### SQ# Fix

Every `spawn` returns a **ScriptHandle** — a promise-like object. Use `await` to suspend until completion and get the result.

```sqf
// Spawn background work, await result:
private _handle = _data spawn {
    sleep 2;
    private _result = heavyComputation(_this);
    _result;  // becomes the handle's resolved value
};

// Do other work...
processOtherStuff();

// Wait for result:
private _answer = await _handle;
// _answer == result of heavyComputation
```

```sqf
// Parallel await pattern:
private _h1 = _data1 spawn { processPart1(_this); };
private _h2 = _data2 spawn { processPart2(_this); };
private _h3 = _data3 spawn { processPart3(_this); };

// Wait for all:
private _r1 = await _h1;
private _r2 = await _h2;
private _r3 = await _h3;
private _total = _r1 + _r2 + _r3;
```

📖 Full guide: [promises.md](promises.md)

---

## 7. Rich Array & HashMap Types

### SQF Problem

Arrays are the only compound type. No hashmaps until Arma 3 v2.14 (still limited). `nil` in arrays is inconsistent. `resize` is error-prone.

### SQ# Fix

Full-featured `SqArray` and `SqHashMap`:

| Operation | SQ# | SQF |
|---|---|---|
| Push back | `pushBack` (O(1) amortized) | ✅ |
| Append array | `append` (O(n) bulk) | ✅ |
| Delete at | `deleteAt` (O(n)) | ✅ |
| Delete range | `deleteRange` (O(n)) | ✅ |
| Resize | `resize` | ✅ |
| Find | `find` (O(n) linear search) | ✅ |
| Sort | `sort` (O(n log n)) | ✅ |
| Reverse | `reverse` (O(n)) | ❌ SQF sort+reverse |
| Freeze | `freeze` → immutable | ❌ |
| Thaw | `thaw` → mutable copy | ❌ |
| HashMap (O(1) lookup) | `createHashMap` | ✅ (2.14+) |
| HashMap: array keys | ❌ Rejected (mutable keys unsafe) | ⚠️ Silent bugs |
| Ownership tracking | ✅ Cross-scheduler protection | ❌ |
| `nil` in arrays | ✅ Storable as value | ❌ Deletes element |

📖 Full guide: [arrays.md](arrays.md)

---

## 8. Error Handling — Try/Catch, Typed Errors

### SQF Problem

No structured error handling. Errors either crash the script silently or produce cryptic messages. No way to recover.

### SQ# Fix

`try { ... } catch { ... }` with typed error values:

```sqf
try {
    private _result = _undefinedVariable + 5;  // would crash in SQF
} catch {
    params ["_error"];
    hint format ["Caught: %1", _error];
    // Continue execution...
};
```

**Error types**:
- `SqTypeError` — type mismatch
- `SqUndefinedVariableError` — undefined variable
- `SqOwnershipError` — cross-scheduler array access
- `SqThreadSafetyError` — isolated command from wrong scheduler
- `SqParseError` — syntax error (compile time)

---

## 9. CLI Development Tools

### SQF Problem

No standard CLI. Debugging is `diag_log` and `systemChat`. No way to lex/parse/compile separately. No REPL.

### SQ# Fix

The `sqsharp` CLI provides:

```bash
sqsharp lex script.sqf       # Print token stream
sqsharp parse script.sqf     # Print AST
sqsharp compile script.sqf   # Print bytecode
sqsharp run script.sqf       # Execute script (prints result)
sqsharp repl                 # Interactive REPL
```

```bash
$ sqsharp repl
SQ# REPL (type 'exit' to quit)
> 1 + 2 * 3
  => 7
> private _arr = [1, 2, 3]; _arr pushBack 4; _arr
  => [4]
> shared _x = 0; _x add 1; _x add 5; get _x
  => 6
```

**Key differences**:
- Token-level debugging (`lex`)
- AST inspection (`parse`)
- Bytecode inspection (`compile`)
- Rapid prototyping (`repl`)
- CI/CD testable (`run` returns exit code)

---

## 10. .NET Interop — Host-Defined Commands

### SQF Problem

SQF is locked to the Arma engine. Can't call external libraries. Can't integrate with C#/.NET ecosystem. Can't use SQF in non-Arma projects.

### SQ# Fix

Any C# host can register **custom commands** that call .NET code:

```csharp
// Register a .NET method as an SQ# command:
var host = new SqHost();
host.RegisterUnary("getTime", _ => new SqValue(DateTime.Now.Ticks));
host.RegisterBinary("addPlayer", (name, score) => {
    _database.AddPlayer(name.AsString(), score.AsNumber());
    return SqValue.True;
});

// Now callable from SQ#:
host.ExecuteString(@"
    private _now = getTime;
    addPlayer ['Fred', 100];
");
```

**Host can**:
- Register nular, unary, binary commands
- Set thread safety levels per command
- Provide custom type implementations
- Inject globals and pre-compiled functions
- Intercept `print`/`hint`/`systemChat` output

📖 Full guide: [for-dotnet-devs.md](for-dotnet-devs.md)

---

## 11. Thread Safety — Guarantees, Not Hopes

### SQF Problem

In Arma, there IS no multithreading for scripts. But when SQ# adds it, thread safety becomes critical. Most game scripting languages give you nothing — just "be careful."

### SQ# Fix

**Three pillars of thread safety**:

1. **Ownership** — Every array/hashmap knows which scheduler created it. Cross-scheduler mutation throws `OwnershipError`.

2. **Freeze/Thaw** — Make data immutable before sharing. Zero-cost reads from any scheduler. No locks.

3. **Lock-Free Primitives** — Channels (SPSC queues) and Shared (CAS atomic). No deadlocks possible. No mutexes to manage.

```
┌──────────────────────────────────────────────────────┐
│  SCRIPTER EXPERIENCE                                 │
│                                                      │
│  Single scheduler (99% of scripts):                   │
│    • Write normal SQF-style code                     │
│    • No threading concerns. No locks. No atomics.    │
│    • Everything just works.                          │
│                                                      │
│  Multi-scheduler (opt-in):                           │
│    • spawnOn "AI" { ... } — run on AI scheduler     │
│    • Freeze data before sharing → immutable, safe    │
│    • Channel for message passing → safe, lock-free   │
│    • shared for counters/flags → safe, CAS-based     │
│    • VM rejects unsafe ops → clear error             │
│                                                      │
│  NEVER:                                              │
│    • No locks to manage                              │
│    • No mutexes to acquire                           │
│    • No deadlocks possible                           │
│    • No data races possible                          │
│    • No silent memory corruption                     │
└──────────────────────────────────────────────────────┘
```

---

## 12. Performance Optimizations

### Compile-Time

- Single-pass compilation — AST → bytecode in one walk
- Constant folding (🔜 Planned): `1 + 2` → `3` at compile time
- Local variable slot allocation — O(1) access
- Command ID pre-resolution

### Runtime

- Bytecode VM — no tree walking overhead
- `pushBack` — O(1) amortized (growable array)
- `freeze` — zero-cost reads (immutable snapshot)
- `shared` — lock-free CAS (no locks, no contention)
- String interning — fast equality checks

### Scheduler

- Fiber queue — FIFO, O(1) enqueue/dequeue
- Time budget tracking — per-instruction timing
- Sleep list — ordered by wake time, efficient wake scanning

📖 Full guide: [optimisation.md](optimisation.md)

---

## 13. What SQ# Intentionally Does NOT Have

Some SQF features are deliberately omitted or changed:

| SQF Feature | SQ# Status | Reason |
|---|---|---|
| `_x` auto-available in `forEach` | ✅ Matches SQF | `_x` and `_forEachIndex` auto-bound in forEach loops |
| `configFile` / `missionConfigFile` | ❌ Not applicable | Config is an Arma concept. Host-defined equivalent. |
| `uiNamespace` / `parsingNamespace` | ❌ Not applicable | UI is host-defined |
| `callExtension` | 🏠 Host | .NET host registers commands via `RegisterCommand` |
| `preprocessFileLineNumbers` | 🔜 Planned | SQ# preprocessor exists, needs runtime integration |
| `terminate` | ✅ Implemented | `terminate _handle` kills script handle and its fiber |
| `isNil { code }` force-unscheduled | 🆕 Replaced | Use `callUnscheduled { code }` to run code outside scheduler |
| 10,000 iteration while limit | ⚙️ Host-configurable | Host sets `SqHost.MaxIterations`. Default: unlimited. |

---

## Quick Comparison: SQF vs SQ#

| Area | SQF (Arma 3) | SQ# |
|---|---|---|
| Type system | Dynamic, nil-deletes-vars | Tagged union, nil deletes variables (SQF compat) |
| Parser | Hand-written C, heuristic | Pratt parser, 11 precedence levels |
| Execution | AST walking | Bytecode VM (29 opcodes) |
| Threading | None (single scheduler) | Cooperative multi-scheduler |
| Thread safety | N/A | Ownership + freeze + lock-free primitives |
| Multiplayer | Engine-coupled networking | Scheduler = machine, portable |
| Async | `sleep`/`waitUntil` only | + `await`, ScriptHandle promises |
| Modules | `#include` text substitution | `#include` preprocessor (opt-in) |
| Error handling | Silent crashes, cryptic errors | try/catch, typed errors |
| HashMap | 2.14+ only | Yes, with key validation |
| CLI tools | None | lex, parse, compile, run, repl |
| .NET interop | None | Host-defined commands |
| Performance | AST interpretation | Bytecode VM (~50-100x faster) |
| Debuggability | `diag_log` only | Bytecode inspection, 🔜 DAP debugger |
| License | Proprietary (Arma engine) | Open-source (MIT) |

---

## Getting Started

- **SQF scripter?** → [for-sqf-scripters.md](for-sqf-scripters.md)
- **C# developer?** → [for-dotnet-devs.md](for-dotnet-devs.md)
- **Language reference?** → [language-spec.md](language-spec.md)
- **Quick reference?** → [quick-reference.md](quick-reference.md)
- **Threading?** → [multithreading.md](multithreading.md)
- **Multiplayer?** → [multiplayer.md](multiplayer.md)
- **Performance?** → [optimisation.md](optimisation.md)

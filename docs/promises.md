# Promise System

## SQF Script Handle Basics

| Feature | Description |
|---|---|
| **Created by** | `spawn`, `execVM` |
| **Status** | `scriptDone` → Boolean. `isNull` (Arma 3) — completed → null. |
| **Termination** | `terminate` |
| **Self-reference** | `_thisScript` (Arma 3 1.54+) |
| **Introduced** | Arma 1 |

## SQF Promise Handles (Arma 3 2.22+)

Script Handles double as Promises. Every spawned script is a promise resolved when the script exits.

| Operation | Syntax | Description |
|---|---|---|
| Empty promise | `spawn "Name"` | No script — promise holder. Resolve via `terminate`. |
| Real promise | `0 spawn { ... }` | Script runs → handle resolves with return value. |
| Resolve | `_h terminate value` | Complete with value. Kills backing script. |
| Await | `_result = waitUntil _h` | Block until resolved. Scheduled only. |
| Await + timeout | `waitUntil [_h, 60]` | nil if timeout. |
| Continuation | `_h continueWith { ... }` | Callback when resolved. `_this` = value. |
| Check | `scriptDone` / `isNull` | Whether resolved. |

```sqf
// SQF promise example
_handle = spawn "MyPromise";
_handle terminate "result value";
_result = waitUntil _handle;  // "result value"
_handle continueWith { systemChat format ["Got: %1", _this]; };
```

---

## SQ# Promise System — Enhanced for Multithreading

Every Script Handle IS a `Task<SqValue>` under the hood.

### Architecture

```
ScriptHandle<T>
├── SQF compat: scriptDone, terminate, isNull, continueWith
└── .NET Task: .ToTask(), .ContinueWith(), .GetAwaiter(), CancellationToken

Fiber binding:
  Scheduler A (Thread 1)          Scheduler B (Thread 2)
  [Fiber: waitUntil _h]           [Fiber: spawn {cpu}]
       ↓                               ↓
  Fiber SUSPENDED                Fiber RUNNING
  (awaiting promise)             (CPU-bound work)
       ↓                               ↓
  Fiber RESUMED                  Promise RESOLVED
  (on Scheduler A)               (signal back to A)
```

### Key Enhancements Over SQF

| Feature | SQF | SQ# |
|---|---|---|
| Threading | Same scheduler only | Cross-scheduler, cross-thread |
| C# interop | Not possible | `await handle.ToTask()` |
| Cancellation | `terminate` only | `CancellationToken` |
| Error handling | Silent fail | Exceptions propagate |
| Continuation scheduling | Same scheduler | Any scheduler / thread pool |
| Combinators | None | `PromiseAll`, `PromiseRace`, `PromiseAny` |
| Timeout | `waitUntil [h, t]` | `.Timeout(seconds)` → exception |
  Unscheduled await | ❌ (must use continueWith) | ✅ `await _handle` in any fiber (suspends fiber, not thread) |
| Progress | ❌ | `IProgress<T>` |

### Promise Combinators

```sqf
// All — wait for all
private _results = PromiseAll [_h1, _h2, _h3];
// _results = [result1, result2, result3]

// Race — first wins, others cancelled
private _winner = PromiseRace [_h1, _h2];

// Any — first success (ignore errors unless all fail)
private _first = PromiseAny [_h1, _h2, _h3];
```

### C# Host Integration

```csharp
var handle = vm.Spawn(codeBlock, scheduler: SchedulerA);
SqValue result = await handle.ToTask();

handle.ContinueWith(val => Console.WriteLine($"Result: {val}"));

var cts = new CancellationTokenSource();
var handle = vm.Spawn(codeBlock, cancellationToken: cts.Token);
cts.Cancel();  // equivalent to terminate

var result = await vm.Spawn(codeBlock)
    .Timeout(TimeSpan.FromSeconds(5))
    .ContinueWith(val => val.AsNumber() * 2)
    .ToTask();
```

### SQ# Script Syntax

```sqf
// Spawn on specific scheduler (array on right: [scheduler, code])
private _handle = spawnOn ["AI", { heavyComputation() }];

// With arguments (binary: args spawnOn [scheduler, code])
private _handle = _args spawnOn ["AI", { params ["_x"]; process(_x); }];

// Thread pool for CPU work
private _handle = spawnParallel { expensiveMath() };

// Await (fiber suspends cooperatively)
private _result = await _handle;           // simple await
private _result = await [_handle, 5];      // with timeout (array form)

// Continuation (non-blocking)
_handle continueWith { systemChat str _this; };
_handle continueWithOn ["Main", { updateUI(_this); }];

// Combinators
private _allDone = PromiseAll [
    spawn { loadAssets1() },
    spawn { loadAssets2() }
];

// Error handling
try {
    private _result = await _handle;
} catch (_error: ScriptTimeoutError) {
    systemChat "Timed out!";
}

// Progress
private _handle = spawn {
    for "_i" from 0 to 100 do {
        progress _i;
        sleep 0.01;
    };
    return "done";
};
_handle onProgress { hint format ["%1%%", _this]; };
```

### Scheduler-Aware Spawning

```sqf
_h1 = spawn { ... };                          // current scheduler
_h2 = spawnOn ["AI", { ... }];                // named scheduler
_h3 = spawnOn ["Main", { ... }];              // main/UI thread
_h4 = spawnOn ["IO", { ... }];                // I/O scheduler
_h5 = spawnParallel { heavyMath() };          // .NET thread pool
// ⚠️ spawnParallel: host commands may be unavailable
// Use for pure computation only.
```

### ScriptHandle API (StdLib)

| Command | Signature | Description |
|---|---|---|
| `scriptDone` | `Handle → Boolean` | Resolved? |
| `isNull` | `Handle → Boolean` | Null/resolved? (Arma 3 compat) |
| `terminate` | `Handle, [Value] → Nothing` | Resolve + kill. Optional value. |
| `continueWith` | `Handle, Code → Handle` | Add continuation. Chainable. |
| `continueWithOn` | `Handle, [Scheduler, Code] → Handle` | Continuation on scheduler (SQ#) |
| `waitUntil` | `Handle → Value` | Block until resolved. |
| `waitUntil` | `[Handle, Number] → Value` | Block with timeout. |
| `await` | `Handle → Value` or `[Handle, Number] → Value` | SQ# keyword. Array form for timeout. |
| `PromiseAll` | `[Handle...] → [Value...]` | All resolve (SQ#) |
| `PromiseRace` | `[Handle...] → Value` | First wins (SQ#) |
| `PromiseAny` | `[Handle...] → Value` | First success (SQ#) |
| `spawnOn` | `[Scheduler, Code] → Handle` | Named scheduler. Right = array [name, code] (SQ#) |
| `spawnParallel` | `Code → Handle` | Thread pool (SQ#) |
| `progress` | `Number → Nothing` | Report progress (SQ#) |
| `onProgress` | `Handle, Code → Handle` | Progress callback (SQ#) |

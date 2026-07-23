# SQ# Multithreading

> Cooperative fibers, scheduler isolation, lock-free sharing.  
> SQ# multithreading brings safe concurrency to SQF — without the fear.

## Overview

SQF (Arma 3) is single-threaded. All scripts run on one scheduler. Fine for small missions, crushing for heavy workloads.

SQ# adds **cooperative multithreading**. Not raw OS threads. Not `System.Threading.Thread`. Instead: lightweight fibers scheduled by isolated schedulers, sharing data through lock-free primitives.

```
┌──────────────────────────────────────────────────────────────┐
│                      HOST PROCESS                            │
│                                                              │
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────┐ │
│  │ Scheduler "Main" │  │ Scheduler "AI"  │  │ Scheduler    │ │
│  │ (ID=1)           │  │ (ID=2)          │  │ "Physics"(3) │ │
│  │                  │  │                 │  │              │ │
│  │ Fibers: 3        │  │ Fibers: 12      │  │ Fibers: 1    │ │
│  │ Globals: own     │  │ Globals: own    │  │ Globals: own │ │
│  │ Budget: 3ms      │  │ Budget: 3ms     │  │ Budget: 5ms  │ │
│  └────────┬─────────┘  └────────┬────────┘  └──────┬───────┘ │
│           │                     │                   │         │
│           └──────────┬──────────┴───────────────────┘         │
│                      │                                        │
│              ┌───────▼────────┐                               │
│              │  SHARED STATE  │                               │
│              │  • Channels    │  (lock-free)                  │
│              │  • Shared<T>   │  (CAS-based)                  │
│              │  • Frozen data │  (immutable)                  │
│              └────────────────┘                               │
└──────────────────────────────────────────────────────────────┘
```

**Key rule**: If you never use `spawnOn`, you never see concurrency. Everything runs on the main scheduler — just like SQF. Threading is opt-in.

---

## Concepts

### Scheduler

A `SqScheduler` is a lightweight execution context. Think of it as an "Arma machine" — it has:
- Its own **global namespace** (variables declared with `global` are scheduler-local)
- Its own **fiber queue** (FIFO ready list)
- A **time budget** (default 3ms per `Tick()`)
- A **scheduler ID** for ownership tracking

```csharp
// C# host creates schedulers:
var host = new SqHost();
host.CreateScheduler("AI", budgetMs: 3.0);
host.CreateScheduler("Physics", budgetMs: 5.0);
```

```sqf
// SQ# script targets schedulers:
spawnOn ["AI", { heavyComputation(_this) }];
spawnOn ["Physics", { simulate(_this) }];
```

### Fiber

A `SqFiber` is a single running script. Each fiber has:
- Its own **VM instance** (stack, instruction pointer, locals)
- A **state**: Ready, Running, Waiting, Completed, Terminated
- An optional **wait condition** (sleep time, script handle, channel data)

```
Fiber Lifecycle:
  Spawned → Ready → Running → (yield) → Waiting → Ready → Running → Completed
                                    ↓
                                Terminated (error)
```

### Cooperative Scheduling

Fibers are NOT preempted by the OS. They **cooperate** — they run until:
1. They complete (return a value)
2. They yield (`sleep`, `waitUntil`, channel `receive`)
3. The scheduler's time budget is exhausted (3ms by default)

This is the SQF model. No race conditions mid-instruction. No need for mutexes.

```
Tick() — runs every frame:
  ┌─ while (budget > 0 && ready queue not empty):
  │    fiber = ready.Dequeue()
  │    fiber.ExecuteStep()         // one bytecode instruction
  │    budget -= stepTime
  │    if fiber completed:
  │       resolve its handle
  │    else if fiber yielded:
  │       move to waiting list
  │    else:
  │       re-enqueue (budget exhausted)
  └─ check waiting fibers:
       wake any whose sleep/condition is done
```

---

## Script Execution Models

### `call` — Inherit Environment

```sqf
// Runs in caller's scheduler. No new fiber. Shares parent locals.
_result = _args call { _this + 1; };
```

| Property | Behavior |
|---|---|
| Scheduler | Inherits caller's scheduler |
| Suspension | Only if caller is scheduled |
| Parent locals | ✅ Accessible |
| Returns | Last expression value |
| Use for | Fast, synchronous computation |

### `spawn` — New Fiber, Same Scheduler

```sqf
// Creates a fiber on the CURRENT scheduler.
_handle = _data spawn { sleep 5; process(_this); };
```

| Property | Behavior |
|---|---|
| Scheduler | Current scheduler |
| Suspension | Always allowed |
| Parent locals | ❌ Not accessible. Pass via `params`. |
| Returns | ScriptHandle (promise) |
| Use for | Background work, delayed execution |

### `spawnOn` — New Fiber, Specific Scheduler

```sqf
// Creates a fiber on a DIFFERENT scheduler.
_handle = _data spawnOn ["AI", {
    params ["_d"];
    while { alive _d } do {
        _d add 1;
        sleep 0.1;
    };
}];
```

| Property | Behavior |
|---|---|
| Scheduler | Specified scheduler |
| Suspension | Always allowed |
| Parent locals | ❌ |
| Returns | ScriptHandle |
| Use for | Cross-scheduler work, parallel computation |

**⚠️ Data passed to `spawnOn` must be safe to share.** Frozen arrays, channels, or Shared values. Raw mutable arrays from another scheduler will throw `OwnershipError`.

---

## Sleep, Yield & Await

### `sleep` — Time-Based

```sqf
sleep 2.5;         // suspend fiber for 2.5 seconds
sleep 0.01;        // yield to next frame (minimum)
```

The fiber moves to the waiting list. Wakes up when `scheduler.CurrentTime ≥ sleepTarget`.

### `waitUntil` — Condition-Based

```sqf
waitUntil { alive _unit };           // check each frame
waitUntil { sleep 0.5; _x > 100 };  // check every 0.5s (efficient!)
```

Without a `sleep` inside, `waitUntil` checks every frame (3ms budget). Add `sleep` for efficient polling.

### `await` — Promise-Based

```sqf
_handle = _data spawn { sleep 2; _this * 2; };
_result = await _handle;   // wait for fiber to complete, get result
```

`await` suspends the current fiber until the target fiber completes. Returns the target's result.

### `canSuspend` Check

```sqf
if (canSuspend) then {
    sleep 1;        // safe
} else {
    // running unscheduled — use isNil { sleep 1; }
};
```

`canSuspend` returns `true` in scheduled environment (spawn), `false` in unscheduled (call from unscheduled).

---

## Thread Safety Model

### Rule 1: Schedulers Don't Share Mutable State

Each scheduler has its OWN global namespace. `global X = 5` on scheduler "Main" does NOT affect `global X` on scheduler "AI".

```sqf
// Scheduler "Main":
global COUNTER = 0;
COUNTER = COUNTER + 1;    // COUNTER == 1

// Scheduler "AI" (different):
COUNTER = COUNTER + 1;    // COUNTER == 1 (its own copy!)
```

**This eliminates 90% of potential data races.** Scripter who never uses `spawnOn` never encounters shared state.

### Rule 2: Arrays Have Owners

Every `SqArray` is created with an owner scheduler ID. Only the owner can mutate it.

```sqf
// Scheduler "Main":
_data = [1, 2, 3, 4, 5];

// Scheduler "AI":
_data set [0, 99];
// ❌ OwnershipError: Array owned by scheduler 1, accessed from scheduler 2
```

### Rule 3: Share Through Primitives

Three lock-free primitives for cross-scheduler sharing:

| Primitive | Use Case | Lock-Free |
|---|---|---|
| `freeze` / `thaw` | Immutable read-only sharing | ✅ (zero-cost reads) |
| `Channel<T>` | Message passing, producer/consumer | ✅ (SPSC queue) |
| `shared` (Shared<T>) | Atomic counters, flags, status | ✅ (CAS) |

---

## Sharing Primitives

### 1. Freeze — Immutable Snapshot

```sqf
// Create data on Main scheduler:
_data = [1, 2, 3, 4, 5];
_frozen = freeze _data;

// Pass to AI scheduler — SAFE:
[_frozen] spawnOn ["AI", {
    params ["_d"];
    _sum = _d select 0;                // ✅ read OK
    _d set [0, 99];                    // ❌ Error: FrozenArray is immutable
}];

// Get mutable copy back:
_local = thaw _frozen;                  // new mutable array on current scheduler
_local set [0, 99];                     // ✅ OK
```

**How it works**: `freeze` copies the array to an immutable structure (`System.Collections.Immutable`). Zero-cost reads from any scheduler. No locks, no synchronization overhead.

`freeze` works on: arrays, hashmaps, code blocks.

### 2. Channel<T> — Message Passing

```sqf
// Create channel (accessible from any scheduler):
_ch = channel;

// Scheduler "Main" — producer:
_ch send ["damage", _unit, 0.75];

// Scheduler "AI" — consumer:
_data = _ch receive;   // suspends fiber until data arrives
// _data == ["damage", _unit, 0.75]

// Non-blocking check:
if (_ch canReceive) then {
    _data = _ch receive;
};
```

**How it works**: Lock-free SPSC (single-producer, single-consumer) queue. Uses `ConcurrentQueue<T>` under the hood. `receive` suspends the fiber cooperatively (not the thread) until data arrives.

**Two-way communication** — pass a response channel:

```sqf
_replyCh = channel;
_ch send [_request, _replyCh];
_response = _replyCh receive;   // wait for reply
```

**Multi-consumer**: Use one channel per consumer, or use `broadcast` pattern with multiple channels.

### 3. Shared — CAS-Based Atomic

```sqf
// Declare shared variable (like `private` but atomic):
shared _counter = 0;

// Any scheduler can atomically update:
spawnOn ["AI", {
    _counter add 1;          // Interlocked.Increment — returns new value
}];

spawnOn ["Physics", {
    _counter add 5;          // atomic add
    _counter sub 2;          // atomic subtract
}];

// Read current value:
_val = get _counter;         // explicit atomic read
_val = _counter + 0;         // auto-unwrap in arithmetic

// Compare-and-swap:
_swapped = _counter compareSwap [42, 99];
// if _counter == 42, set to 99 and return true
// else return false

// Direct set:
_counter set 0;              // atomic write

// Boolean shared:
shared _flag = false;
_flag set true;
_flag compareSwap [false, true];  // only set if still false
```

**Supported operations**:

| Operation | Number | Boolean | Description |
|---|---|---|---|
| `add` | ✅ | ❌ | Atomic increment |
| `sub` | ✅ | ❌ | Atomic decrement |
| `set` | ✅ | ✅ | Atomic write |
| `get` | ✅ | ✅ | Atomic read |
| `compareSwap` | ✅ | ✅ | CAS: `[expected, new]` |
| `+`, `-`, `*`, `/` | ✅ | ❌ | Auto-unwrap in arithmetic |
| `==`, `!=`, `<`, `>` | ✅ | ❌ | Auto-unwrap in comparison |

**How it works**: `Interlocked.CompareExchange(ref double, ...)` with CAS spin-loop. No locks. Wait-free for reads, lock-free for writes.

---

## Ownership Model Deep Dive

### Array Ownership

| Operation | Rule | Error on Violation |
|---|---|---|
| Create array | Owned by creating fiber's scheduler | — |
| Read own array | ✅ Any fiber on same scheduler | — |
| Read foreign mutable array | ❌ | `OwnershipError` |
| Mutate foreign array | ❌ | `OwnershipError` |
| `freeze` | Creates immutable copy — any scheduler can read | `ImmutableError` on mutation |
| `thaw` | Creates new owned mutable copy | — |
| `sendTo(scheduler)` | Transfers ownership — source loses access | `OwnershipError` after transfer |
| `copy` | Creates new owned copy in current scheduler | — |

### HashMap Ownership

Same rules as arrays. Keys must NOT be mutable arrays.

### Code Block Ownership

Code blocks are scheduler-agnostic. They have no mutable state. Free to share.

---

## Host Command Thread Safety

Host registers each command with a safety level. The VM enforces at call time.

| Level | Meaning | Example Commands |
|---|---|---|
| `Isolated` (default) | Only callable from owning scheduler | `setPos`, `setDamage`, `createVehicle` |
| `ReadOnly` | Safe from any scheduler | `getPos`, `alive`, `count`, `typeName` |
| `Synchronized` | Internal locking, safe from any scheduler | `setVariable`, `getVariable` |
| `MainThread` | Only callable from main/UI scheduler | UI commands, Unity API |
| `Unsafe` | No guarantees — advanced hosts only | Direct memory access, unsafe interop |

```csharp
host.RegisterUnary("getPos", obj => obj.Position, ThreadSafety.ReadOnly);
host.RegisterBinary("setPos", ..., ThreadSafety.Isolated);
```

When a violation occurs:
```
ThreadSafetyError: Command 'setPos' is isolated to scheduler 'Main'.
Current scheduler is 'AI'.
```

---

## Common Patterns

### Pattern 1: Parallel Computation

```sqf
// Split work across schedulers, combine results:
_data = freeze _bigArray;
_ch = channel;

spawnOn ["AI", {
    params ["_data", "_ch"];
    _result = processHalf(_data select [0, 500]);
    _ch send _result;
}];

spawnOn ["Physics", {
    params ["_data", "_ch"];
    _result = processHalf(_data select [500, 500]);
    _ch send _result;
}];

_result1 = _ch receive;
_result2 = _ch receive;
_final = _result1 + _result2;
```

### Pattern 2: Background Worker

```sqf
// AI runs continuously on its own scheduler:
spawnOn ["AI", {
    while { true } do {
        _cmd = _cmdChannel receive;   // wait for work
        processCommand(_cmd);
        _replyChannel send "done";
        sleep 0.01;                   // yield to avoid hogging
    };
}];
```

### Pattern 3: Atomic Counter (Lock-Free)

```sqf
// Track kills across all schedulers without locks:
shared _totalKills = 0;

// Each scheduler increments when a kill happens:
_unit addEventHandler ["Killed", {
    _totalKills add 1;
}];

// Display thread reads atomically:
spawnOn ["UI", {
    while { true } do {
        hint format ["Kills: %1", get _totalKills];
        sleep 1;
    };
}];
```

### Pattern 4: Producer/Consumer Pipeline

```sqf
// Main scheduler produces work:
_workQueue = channel;
_resultQueue = channel;

for "_i" from 0 to 99 do {
    _workQueue send _i;
};

// Worker consumes:
spawnOn ["AI", {
    while { _workQueue canReceive } do {
        _task = _workQueue receive;
        _result = heavyCompute(_task);
        _resultQueue send _result;
    };
}];

// Main collects results:
while { _resultQueue canReceive } do {
    _r = _resultQueue receive;
    _results pushBack _r;
};
```

---

## Pitfalls & Anti-Patterns

### ❌ DON'T: `while { true }` Without `sleep`

```sqf
// BAD — eats entire 3ms budget every frame:
spawnOn ["AI", {
    while { true } do {
        _x = _x + 1;    // runs as fast as possible, starves other fibers
    };
}];

// GOOD — yield to other fibers:
spawnOn ["AI", {
    while { true } do {
        _x = _x + 1;
        sleep 0.01;     // let other fibers run
    };
}];
```

### ❌ DON'T: Pass Mutable Arrays to Other Schedulers

```sqf
// BAD:
_data = [1, 2, 3];
[_data] spawnOn ["AI", { params ["_d"]; _d set [0, 99]; }];
// ❌ OwnershipError

// GOOD:
_frozen = freeze _data;
[_frozen] spawnOn ["AI", { params ["_d"]; /* read only */ }];
```

### ❌ DON'T: Assume `spawn` Order

```sqf
// BAD — races on spawn completion:
_a spawn { sleep 1; global X = 5; };
_b spawn { sleep 0.5; global X = 10; };
// X might be 5 or 10 — no ordering guarantee

// GOOD — use await for ordering:
_h1 = _a spawn { sleep 1; 5; };
_h2 = _b spawn { sleep 0.5; 10; };
await _h2;   // wait for _b first
await _h1;   // then wait for _a
```

### ❌ DON'T: Shared on Complex Types

```sqf
// BAD — shared only works with Number and Boolean:
shared _arr = [1, 2, 3];
// ❌ SqTypeError: Shared value does not support type Array

// GOOD — use freeze for immutable sharing:
_frozen = freeze [1, 2, 3];
```

---

## Debugging

### Fiber Inspection

```csharp
// C# host can inspect fibers:
foreach (var fiber in scheduler.GetAllFibers())
{
    Console.WriteLine($"[{fiber.Id}] {fiber.Name} — {fiber.State} " +
                      $"(ran {fiber.TotalExecutionMs:F2}ms)");
}
```

### Common Errors

| Error | Cause | Fix |
|---|---|---|
| `OwnershipError` | Accessing array from wrong scheduler | `freeze` before sharing, or `copy` |
| `ThreadSafetyError` | Calling isolated command from wrong scheduler | Use `spawnOn` to correct scheduler |
| `ImmutableError` | Mutating a frozen array | `thaw` to get mutable copy |
| `SqTypeError: Shared value does not support type X` | Creating shared with non-number/bool | Only Numbers and Booleans supported |

### Scheduler Stats

```csharp
Console.WriteLine(scheduler.GetStats());
// Scheduler "AI": 12 fibers (3 ready, 5 waiting, 4 completed)
// Budget: 3.0ms, Avg step: 0.12ms, Max step: 1.4ms
```

---

## Quick Reference

| Concept | SQ# Syntax | Notes |
|---|---|---|
| Spawn on current | `_h = _data spawn { ... };` | New fiber, same scheduler |
| Spawn on specific | `_h = _data spawnOn ["AI", { ... }];` | Cross-scheduler |
| Call (inherit) | `_r = _data call { ... };` | No new fiber |
| Sleep | `sleep 2.5;` | Seconds, scheduled only |
| Wait for condition | `waitUntil { condition };` | Checks each frame |
| Await fiber | `_r = await _handle;` | Block until fiber done |
| Frozen array | `_f = freeze _arr;` | Immutable, shareable |
| Mutable copy | `_m = thaw _f;` | From frozen |
| Channel create | `_ch = channel;` | Lock-free SPSC |
| Channel send | `_ch send _data;` | Non-blocking |
| Channel receive | `_d = _ch receive;` | Suspends fiber |
| Shared variable | `shared _x = 0;` | Atomic, CAS-based |
| Atomic add | `_x add 1;` | Returns new value |
| Atomic read | `get _x;` or `_x + 0` | Explicit or auto-unwrap |
| Compare-swap | `_x compareSwap [old, new];` | Returns bool |
| Ownership transfer | `_arr sendTo sched;` | Source loses access |
| Ownership copy | `_new = _arr copy;` | New owned copy |

---

## Architecture: Under the Hood

```
┌─────────────────────────────────────────┐
│ SqHost                                   │
│  • Manages schedulers dictionary         │
│  • Registers commands with safety levels │
│  • Provides tick loop                    │
├─────────────────────────────────────────┤
│ SqScheduler (per scheduler)              │
│  • Ready fiber queue (FIFO)              │
│  • Waiting fiber list (sleep/condition)  │
│  • Completed fiber list                  │
│  • Command registry (injected by host)   │
│  • Time budget tracking                  │
├─────────────────────────────────────────┤
│ SqFiber (per script)                     │
│  • SqVm instance (own stack, IP, locals) │
│  • State machine                         │
│  • ScriptHandle (promise for result)     │
│  • Execution timing stats                │
├─────────────────────────────────────────┤
│ SqVm (per fiber)                         │
│  • Bytecode interpreter                  │
│  • 28 opcodes (Push, Call, Spawn, etc.)  │
│  • Command dispatch (nular/unary/binary) │
│  • Ownership enforcement                 │
│  • Thread safety enforcement             │
├─────────────────────────────────────────┤
│ Sharing Primitives                       │
│  • SqSharedValue (CAS, Interlocked)      │
│  • Channel (ConcurrentQueue)             │
│  • FrozenArray (ImmutableArray)          │
└─────────────────────────────────────────┘
```

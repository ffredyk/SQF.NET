# Scheduler & Thread Safety

## SQF Scheduler (Original Reference)

The SQF scheduler runs scripts cooperatively with a **3ms per frame** time budget.

| Concept | Description |
|---|---|
| **Scheduled** | Scripts in scheduler queue. Each frame runs until 3ms exhausted. Longest-waiting runs first. Paused mid-execution if budget exceeded. |
| **Unscheduled** | Code executes immediately. No suspension allowed. Fast but can freeze. `while` limited to 10,000 iterations. |
| **Suspension** | `sleep`, `uiSleep`, `waitUntil` — ONLY scheduled. `canSuspend` checks. |

**Where code starts scheduled**: `init.sqf`, `spawn`, `execVM`, `call` from scheduled.

**Where code starts unscheduled**: triggers, waypoints, event handlers, preInit, FSMs, object init fields, `call` from unscheduled, `isNil { code }`.

## call vs spawn vs execVM

| Command | Environment | Returns | Suspension | Scope |
|---|---|---|---|---|
| `call code` | **Inherits** caller's env | Last expression value | Only if already scheduled | Parent locals + `_this` |
| `spawn code` | Always **scheduled** | ScriptHandle | ✅ Yes | NO parent locals. Params only. |
| `execVM "path"` | Always **scheduled** | ScriptHandle | ✅ Yes | = `spawn compile preprocessFileLineNumbers` |

**Critical**: `call` does NOT change environment. `call` in scheduled → scheduled. `call` in unscheduled → unscheduled.

**Force unscheduled**: `isNil { code }`.

**spawn order**: SQF does NOT guarantee order. SQ# guarantees FIFO within same scheduler.

## compile / compileScript / preprocessFileLineNumbers

```sqf
_code = compile "hint str _this;";                          // String → Code
_code = compile preprocessFileLineNumbers "script.sqf";      // File → Code (preprocessed)
_code = compileScript ["script.sqf", false, ""];             // File → Code (supports .sqfc)
_string = loadFile "data.txt";                               // Raw file read
_string = preprocessFileLineNumbers "script.sqf";            // Preprocessed string
_string = toString _code;                                    // Code → String (Arma 3 2.02+)
```

## SQ# Scheduler Design

| Concept | SQ# |
|---|---|
| **Time budget** | Configurable per scheduler. Default 3ms. |
| **Scheduled fibers** | Ready/active/waiting queues. Round-robin with budget enforcement. |
| **Unscheduled** | `call` runs in-place. No budget, no suspension. |
| **Suspension** | `sleep`, `waitUntil`, `await` — suspend fiber, release to scheduler. |
| **canSuspend** | Nular → Boolean. True if current fiber is scheduled. |
| **Main scheduler** | Special "Main" for UI/Unity main thread. Pumped by host game loop. |
| **Background schedulers** | Each on own thread. Auto-pumped. |
| **spawn order** | FIFO within same scheduler. |

```
┌─────────────────────────────────────────────────────┐
│ Frame Tick (host game loop)                         │
│                                                     │
│ 1. Host pumps Main scheduler (3ms budget)           │
│    Fiber A runs 1.2ms → yields                      │
│    Fiber B runs 1.5ms → yields                      │
│    Fiber C starts at 2.7ms → budget exhausted        │
│                                                     │
│ 2. Background schedulers auto-tick (own threads)    │
│    Thread 2: AI Scheduler                           │
│    Thread 3: IO Scheduler                           │
│                                                     │
│ 3. Host renders frame                               │
│ 4. Next frame: Main scheduler resumes Fiber C       │
└─────────────────────────────────────────────────────┘
```

---

## Thread Safety — Implicit, Seamless, Zero-Burden

### Design Philosophy

Thread safety is **SQ#'s responsibility, not the scripter's**. Three principles:

1. **Local by default** — Everything belongs to current scheduler. No sharing unless explicit.
2. **Safe sharing is explicit** — Three safe primitives: `Freeze`, `Channel`, `Shared`.
3. **VM enforces** — Cross-scheduler mutation → `OwnershipError`. Unsafe command → `ThreadSafetyError`.

### Ownership Model

```
Scheduler "Main" (Thread 1)
  Fiber A ──owns──▶ [1, 2, 3]        ← mutable
  global SCORE = 42                   ← scheduler-local!
  ── .freeze() ──▶ FrozenArray        ← any scheduler can read
  ── .sendTo("AI") ──▶ ownership transferred

Scheduler "AI" (Thread 2)
  Fiber C ──owns──▶ [4, 5, 6]        ← separate array
  global SCORE = 100                  ← separate value!
  Reads FrozenArray safely
  Receives via Channel
```

### Global Variables Are Scheduler-Local

```sqf
// Each scheduler gets its OWN copy of missionNamespace
// Scheduler "Main":
global COUNTER = 0;
COUNTER = COUNTER + 1;  // COUNTER == 1

// Scheduler "AI" (different thread):
COUNTER = COUNTER + 1;  // COUNTER == 1 (its own copy!)

// Cross-scheduler sharing requires shared:
shared SHARED_COUNTER = 0;
// Read: get SHARED_COUNTER → number; arithmetic auto-unwraps: SHARED_COUNTER + 0
```

This eliminates 90% of potential data races. Scripter who never uses `spawnOn` never encounters shared state.

### Three Safe Sharing Primitives

#### 1. Freeze — Immutable Snapshot
```sqf
private _frozen = _data freeze;
// Readable from ANY scheduler. Zero-cost reads, no locks.
_frozen set [0, 99];  // ERROR: FrozenArray is immutable
```

#### 2. Channel<T> — Message Passing
```sqf
private _channel = Channel create;
_channel send _data;              // send
private _received = _channel receive;  // receive (suspends fiber cooperatively)
if (_channel canReceive) then { ... }; // non-blocking check
```
Lock-free SPSC queue. Two-way pattern via response channel. No deadlocks possible.

#### 3. Shared — CAS-Based Mutable
```sqf
shared _counter = 0;
_counter add 1;                   // atomic (Interlocked.Increment)
private _current = get _counter;  // atomic read (or _counter + 0)
_counter compareSwap [42, 99];    // CAS
```
For counters, flags, status. No locks. `Interlocked`/`Volatile` under the hood.

### Host Command Thread Safety

| Level | Meaning | Example |
|---|---|---|
| `Isolated` (default) | Only owning scheduler | `setPos`, `setDamage` |
| `ReadOnly` | Any thread | `getPos`, `alive`, `count` |
| `Synchronized` | Internal locking, any thread | `setVariable`, `getVariable` |
| `MainThread` | Main/UI only | UI commands, Unity API |
| `Unsafe` | No guarantees (advanced) | Direct memory |

```csharp
host.RegisterUnary("getPos", obj => obj.Position, ThreadSafety.ReadOnly);
host.RegisterBinary("setPos", ..., ThreadSafety.Isolated);
```

### Array/Compound Type Safety

| Operation | Rule | Violation |
|---|---|---|
| Create array | Owned by creating scheduler | — |
| Read own array | ✅ | — |
| Read other's mutable array | ❌ | `OwnershipError` |
| Mutate other's array | ❌ | `OwnershipError` |
| `freeze` | Creates immutable snapshot | `ImmutableError` on mutation |
| `sendTo(scheduler)` | Transfers ownership | `OwnershipError` after transfer |
| `copy` | New owned copy | — |

### Scripter Experience

```
Single scheduler (99% of scripts):
  ✅ Write normal SQF code
  ✅ No threading concerns. No locks. No atomics.
  ✅ Everything just works.

Multi-scheduler (opt-in):
  ✅ spawnOn ["AI", { ... }] — run on AI thread
  ✅ Freeze data → immutable, safe sharing
  ✅ Channel → message passing, lock-free
  ✅ Shared<T> → counters/flags, CAS-based
  ✅ VM rejects unsafe ops → clear error

NEVER:
  ❌ No locks to manage
  ❌ No mutexes to acquire
  ❌ No deadlocks possible
  ❌ No data races possible
  ❌ No silent memory corruption
```

### Thread Safety Commands (StdLib)

| Command | Description |
|---|---|
| `freeze` / `thaw` | Immutable snapshot / mutable copy |
| `sendTo(scheduler)` | Transfer ownership |
| `copy` | New owned copy |
| `Channel create` / `send` / `receive` / `canReceive` | Message passing |
| `shared` / `add` / `sub` / `compareSwap` | CAS-based sync |
| `isFrozen` | Check immutability |
| `owner` | Get owning scheduler |

### Implementation

- **Ownership**: `SchedulerId` (8 bytes) on each array/hashmap/code. Checked on mutation.
- **Freeze**: `System.Collections.Immutable` for arrays.
- **Channel**: Lock-free SPSC queue. Fiber suspension via `TaskCompletionSource`.
- **Shared<T>**: `Interlocked`/`Volatile` wrapper. Zero additional heap allocation.
- **Globals**: Per-scheduler `Dictionary<string, SqValue>`. No sharing unless `Shared<T>`.
- **Performance**: Zero overhead single-scheduler. Overhead only when crossing scheduler boundary.

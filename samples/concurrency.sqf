// ============================================================
// concurrency.sqf — spawnOn, Channels, Shared, Freeze, Thread Safety
// SQ# Language Sample — Multithreading (opt-in!)
// ============================================================

// ⚠️  If you never use spawnOn — you never see concurrency.
// Everything runs on the main scheduler, just like SQF.
// Threading is opt-in.

// ---- spawnOn — Run on a named scheduler ----
// Strict arity rule: every command takes exactly ONE right-side expression.
// spawnOn needs scheduler name + code → MUST wrap both in array.

// Unary form (no arguments):
private _aiHandle = spawnOn ["AI", {
    private _result = heavyPathfinding();
    return _result;
}];

// Binary form (pass arguments on left side):
[100, 200] spawnOn ["AI", {
    params ["_a", "_b"];
    private _sum = _a + _b;
    print f"AI computed: {_sum}";
    return _sum;
}];

// ---- spawnParallel — Run on .NET thread pool (true parallelism) ----
// ⚠️  Only for pure computation. Host commands may NOT be available.
private _cpuHandle = spawnParallel {
    private _result = fibonacci(40);
    return _result;
};

// ---- Schedulers create isolated worlds ----
// Each scheduler has its OWN global namespace.
// Scheduler "Main":
global SCORE = 0;
SCORE = SCORE + 1;                       // SCORE = 1 on Main

// Scheduler "AI" — completely separate SCORE:
spawnOn ["AI", {
    global SCORE = 100;                  // Different SCORE! AI's own copy.
    SCORE = SCORE + 1;                   // SCORE = 101 on AI
}];

// ---- Freeze — Immutable snapshot (safe to share) ----
private _config = [1, 2, 3, 4, 5];      // Mutable, owned by current scheduler
private _frozenConfig = _config freeze;  // Immutable — any scheduler can read

// Pass frozen data to another scheduler — SAFE:
_frozenConfig spawnOn ["Physics", {
    params ["_frozen"];
    // Read OK from any thread:
    private _item = _frozen[2];          // 3
    // _frozen[0] = 99;                 // RUNTIME ERROR: FrozenArray is immutable
    print f"Physics read config[2] = {_item}";
}];

// Original still mutable:
_config set [0, 99];                     // OK on original
// _frozenConfig unchanged — freeze is a snapshot

// ---- channel — Lock-free message passing (SQ# keyword, like private/shared) ----
// channel keyword declares and creates a new message channel:
channel _channel;                        // Creates Channel — no initializer needed

// Producer (current fiber):
_channel send "Hello from Main";        // Binary: left=channel, right=data
_channel send [1, 2, 3];

// Consumer (another scheduler):
spawnOn ["AI", {
    // receive blocks fiber cooperatively (not the thread!):
    private _msg = receive _channel;     // Unary: right=channel → "Hello from Main"
    private _data = receive _channel;    // Unary: right=channel → [1, 2, 3]
    print f"AI got: {_msg}, data: {_data}";
}];

// Non-blocking check:
if (canReceive _channel) then {          // Unary: right=channel → bool
    private _val = receive _channel;     // Unary: right=channel
    print f"Got: {_val}";
};

// ---- Two-way request/response pattern ----
private _makeRequest = {
    params ["_input"];
    channel _replyChannel;               // Create reply channel

    [_input, _replyChannel] spawnOn ["Worker", {
        params ["_data", "_reply"];
        private _result = processData(_data);
        _reply send _result;             // Send result back
    }];

    // Wait for response:
    private _response = receive _replyChannel;  // Unary
    _response
};

// ---- shared — Atomic synchronized value (SQ# keyword, like private/channel) ----
// shared keyword declares CAS-based variable with initial value:
shared _counter = 0;                     // Shared<Number>, init 0

// Any scheduler can atomically update:
spawnOn ["AI", {
    _counter add 1;                      // atomic increment (Interlocked)
}];
spawnOn ["Physics", {
    _counter add 5;                      // atomic add
}];

// Read current value (atomic read):
private _current = get _counter;         // Unary: right=shared → returns current value

// Compare-and-swap:
private _swapped = _counter compareSwap [42, 99];
// If counter == 42, set to 99. Returns true if swapped.

// Shared Boolean (flag):
shared _flag = true;                     // Shared<Boolean>, init true
// Atomic toggle via compareSwap:
_flag compareSwap [true, false];         // If true, set to false
// Or explicit set:
_flag set false;                         // Binary: left=shared, right=new value

// ---- Ownership Model ----
// Arrays, HashMaps, Code owned by creating scheduler.
// Cross-scheduler mutation → OwnershipError.

private _myData = [1, 2, 3];            // Owned by current scheduler

// Transfer ownership:
_myData sendTo "AI";                     // Ownership transferred to AI scheduler
// _myData[0] = 5;                      // ERROR: no longer own this array

// Copy to create new owned instance:
private _copyData = _myData copy;        // New array, owned by current scheduler

// ---- Host Command Thread Safety Levels ----
// Host declares safety level per command:
// - Isolated: only callable from owning scheduler
// - ReadOnly: safe from any thread (e.g., getPos, count)
// - Synchronized: internal locking
// - MainThread: only from main/UI scheduler
// - Unsafe: no guarantees

// Example: ReadOnly command OK from any scheduler:
spawnOn ["AI", {
    private _len = count [1, 2, 3];      // count is ReadOnly — OK from any thread
}];

// Example: Isolated command only on owning scheduler:
// spawnOn ["AI", {
//     _obj setPos [0, 0, 0];           // ERROR: setPos Isolated to Main
// }];

// ---- What scripter NEVER does ----
// NO locks to manage
// NO mutexes to acquire
// NO deadlocks possible (channels are lock-free)
// NO data races possible (ownership + freeze + channel)
// NO silent memory corruption

// ---- Safety checks ----
private _isFrozen = isFrozen _frozenConfig;  // true
private _whoOwns = owner _myData;             // Scheduler ID

"concurrency complete"

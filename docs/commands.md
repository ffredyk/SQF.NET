# SQ# Command Reference

Complete reference for all internal commands packaged with SQ#. Commands grouped by category.

---

## Command Arity

Every SQ# command has fixed arity:

| Arity | Signature | Example |
|---|---|---|
| **Nular** | `cmd` — no operands | `nil`, `player`, `true` |
| **Unary** | `cmd <right>` | `count _arr`, `str _val` |
| **Binary** | `<left> cmd <right>` | `_a + _b`, `_arr select 0` |

Commands needing 3+ parameters use an **array** on the right side:
```sqf
_arr set [idx, val]          // 2 params packed into array
spawnOn ["AI", { code }]     // scheduler name + code packed into array
```

---

## 1. Value Constructors

Nular commands that produce primitive values.

| Command | Arity | Returns | Description |
|---|---|---|---|
| `nil` | nular | Nothing | Nil value. Assigning nil to a variable DELETES it (SQF behavior). nil is still a valid array element and return value. |
| `true` | nular | Boolean | Boolean true. |
| `false` | nular | Boolean | Boolean false. |

```sqf
_x = nil;                     // variable DELETED — _x no longer exists
if (true) then { ... };       // boolean literal

// nil as a value still works in arrays and comparisons:
_arr = [1, nil, 3];           // nil in arrays is fine
if (_arr select 1 == nil) then { ... };  // comparison works
```

---

## 2. Arithmetic Operators

All arithmetic auto-unwraps `shared` values. Division/modulo by zero throws `SqTypeError`.

| Command | Arity | Description |
|---|---|---|
| `+` | binary | Addition. Also string concatenation when either operand is string. |
| `-` | binary | Subtraction. |
| `-` | unary | Negation (prefix). |
| `*` | binary | Multiplication. |
| `/` | binary | Division. Zero divisor → error. |
| `%` | binary | Modulo. Zero divisor → error. |
| `^` | binary | Power (exponentiation). Compiled inline to `Math.Pow`. |
| `min` | binary | Smaller of two numbers. |
| `max` | binary | Larger of two numbers. |

```sqf
_result = 3 + 4;              // 7
_result = -5;                 // unary negation
_result = 10 / 3;             // 3.333...
_result = 10 % 3;             // 1
_result = 2 ^ 8;              // 256
_result = 3 min 7;            // 3
_result = 3 max 7;            // 7

// String concatenation via +
_greeting = "Hello " + _name; // "Hello Fred"
```

---

## 3. Comparison Operators

All return Boolean. Auto-unwrap `shared` values. String comparison is ordinal.

| Command | Arity | Description |
|---|---|---|
| `==` | binary | Equal. For Shared values compares unwrapped numbers. |
| `!=` | binary | Not equal. |
| `<` | binary | Less than. |
| `>` | binary | Greater than. |
| `<=` | binary | Less than or equal. |
| `>=` | binary | Greater than or equal. |

```sqf
if (_hp <= 0) then { ... };
if (_name == "player") then { ... };
if (_arr != nil) then { ... };
```

---

## 4. Logical Operators

`&&` and `||` use **lazy short-circuit evaluation** — right side only evaluated if needed. Handled by compiler with code-block short-circuit.

| Command | Arity | Description |
|---|---|---|
| `!` | unary | Logical NOT. |
| `&&` | binary | Logical AND (short-circuit). |
| `\|\|` | binary | Logical OR (short-circuit). |

```sqf
if (_alive && _hp > 50) then { ... };   // _hp only checked if _alive is true
_flag = !_done;                          // invert boolean
```

---

## 5. Array Commands

Arrays are zero-based, dynamic, mutable, by-reference.

| Command | Arity | Thread Safety | Description |
|---|---|---|---|
| `count` | unary | ReadOnly | Array length. Also works on strings. |
| `select` | binary | ReadOnly | Element at index. Out of range → nil. Also works on strings. |
| `pushBack` | binary | — | Append one element. Returns new index. |
| `append` | binary | — | Append all elements from another array. |
| `deleteAt` | binary | — | Remove element at index. Returns deleted value (nil if out of range). |
| `deleteRange` | binary | — | Remove range. Right side = `[startIndex, count]`. |
| `resize` | binary | — | Resize array. Fills new slots with nil, truncates if smaller. |
| `reverse` | unary | — | Reverse array in-place. Returns nil. |
| `sort` | binary | — | Sort array. Right side: `true` = ascending, `false` = descending. |
| `find` | binary | ReadOnly | Find element index. Returns -1 if not found. Also works on strings. |
| `in` | binary | ReadOnly | Contains check. Left = element, right = array/string. |
| `forEach` | binary | — | Iterate array. See [forEach](#foreach) below. |
| `freeze` | unary | ReadOnly | Make array immutable (thread-safe snapshot). |
| `thaw` | unary | — | Create new mutable copy from frozen/regular array. |
| `isFrozen` | unary | ReadOnly | Check if array is frozen. |

```sqf
_arr = [1, 2, 3];
count _arr;                    // 3
_arr select 0;                 // 1
_arr pushBack 4;               // returns 3 (index of new element)
_arr append [5, 6];            // _arr is now [1,2,3,4,5,6]
_arr deleteAt 0;               // returns 1, _arr is [2,3,4,5,6]
_arr deleteRange [1, 2];       // _arr is [2,5,6]
_arr resize 2;                 // _arr is [2,5]
_arr resize 4;                 // _arr is [2,5,nil,nil]
reverse _arr;                  // _arr is [nil,nil,5,2]
_arr sort true;                // ascending: [2,5,nil,nil] (nils sort to end)
_arr find 5;                   // 1
3 in _arr;                     // false

// Frozen arrays (thread-safe sharing)
_frozen = freeze _arr;         // immutable snapshot
_arr2 = thaw _frozen;          // new mutable copy
isFrozen _frozen;              // true
```

### forEach

Binary: `_array forEach { code }`. Inside code block:
- `_x` = current element
- `_forEachIndex` = zero-based index
- `_this` = `[element, index, array]` (SQF compat)

```sqf
[1, 2, 3] forEach {
    systemChat f"Index {_forEachIndex}: {_x}";
};
```

---

## 6. String Commands

| Command | Arity | Thread Safety | Description |
|---|---|---|---|
| `count` | unary | ReadOnly | String length (character count). |
| `select` | binary | ReadOnly | Character at index. Out of range → nil. |
| `find` | binary | ReadOnly | Find substring index. Returns -1 if not found. |
| `in` | binary | ReadOnly | Contains check. Left = substring, right = string. |
| `+` | binary | — | Concatenation (when either operand is string). |
| `str` | unary | — | Convert any value to its string representation. |
| `format` | unary | — | sprintf-style formatting. Right side = `[template, arg1, arg2, ...]`. |
| `parseNumber` | unary | — | Parse string to number. Returns nil on failure. |
| `toArray` | unary | ReadOnly | Convert string to array of character codes. |
| `toString` | unary | ReadOnly | Convert array of character codes to string. |
| `splitString` | binary | — | Split string by separator. Returns array. |
| `joinString` | binary | — | Join string array with separator. Returns string. |
| `toLower` | unary | ReadOnly | Convert to lowercase (invariant culture). |
| `toUpper` | unary | ReadOnly | Convert to uppercase (invariant culture). |
| `trim` | unary | ReadOnly | Remove leading/trailing whitespace. |

```sqf
_name = "Arma";
count _name;                   // 4
_name select 1;                // "r"
_name find "rm";               // 1
"rm" in _name;                 // true
"Hello " + _name;              // "Hello Arma"
str 42;                        // "42"
str [1,2];                     // "[1,2]"
format ["HP: %1/%2", _hp, _max];  // "HP: 75/100"
parseNumber "123.45";          // 123.45
parseNumber "abc";             // nil
toArray "AB";                  // [65, 66]
toString [65, 66];             // "AB"
"a,b,c" splitString ",";       // ["a","b","c"]
// or unary form:
// splitString ["a,b,c", ","];
["a","b"] joinString "-";      // "a-b"
toUpper "hello";               // "HELLO"
toLower "WORLD";               // "world"
trim "  hi  ";                 // "hi"
```

---

## 7. Math Commands

All operate on numbers. Auto-unwrap `shared` values.

| Command | Arity | Description |
|---|---|---|
| `abs` | unary | Absolute value. |
| `floor` | unary | Round down to integer. |
| `ceil` | unary | Round up to integer. |
| `round` | unary | Round to nearest integer (midpoint = even). |
| `sqrt` | unary | Square root. Negative input clamped to 0. |
| `sin` | unary | Sine (radians). |
| `cos` | unary | Cosine (radians). |
| `tan` | unary | Tangent (radians). |
| `asin` | unary | Arc sine (returns radians). |
| `acos` | unary | Arc cosine (returns radians). |
| `exp` | unary | e^x (natural exponential). |
| `log` | unary | Natural logarithm. Input clamped to ε minimum. |
| `atan2` | binary | Arc tangent of y/x (returns radians). |
| `pow` | binary | Raise a to power b. |

```sqf
abs -5;                        // 5
floor 3.7;                     // 3
ceil 3.2;                      // 4
round 3.5;                     // 4
sqrt 16;                       // 4
sin 1.5708;                    // ~1
cos 0;                         // 1
atan2 [1, 1];                  // 0.785... (π/4)
// or binary form:
1 atan2 1;                     // 0.785...
pow [2, 8];                    // 256
// or binary form:
2 pow 8;                       // 256
```

---

## 8. Random Commands

| Command | Arity | Thread Safety | Description |
|---|---|---|---|
| `random` | unary | — | Random float in `[0, max)`. Thread-safe (internal lock). |
| `selectRandom` | unary | ReadOnly | Pick random element from array. Returns nil for empty array. |
| `selectRandomWeighted` | unary | — | Weighted random pick. Right side = `[[val1, weight1], [val2, weight2], ...]`. |

```sqf
_dice = random 6;              // 0.0 to 5.999...
_roll = floor random 6;        // 0 to 5
_color = selectRandom ["red", "green", "blue"];

// Weighted: 70% "common", 25% "rare", 5% "epic"
_loot = selectRandomWeighted [["common", 70], ["rare", 25], ["epic", 5]];
```

---

## 9. Type & Introspection Commands

| Command | Arity | Thread Safety | Description |
|---|---|---|---|
| `typeName` | unary | — | Type name as lowercase string: `"number"`, `"string"`, `"array"`, `"code"`, `"boolean"`, `"nothing"`, `"hashmap"`, `"namespace"`, `"scripthandle"`, `"error"`, `"shared"`, etc. |
| `isNil` | unary | — | Check if value is nil or if variable is undefined. With string arg: check if global variable/command exists (SQF compat). |
| `isNull` | unary | — | Check if ScriptHandle is resolved or value is nil. |
| `str` | unary | — | Convert any value to string. Nil → `"nil"`. |

```sqf
typeName 42;                   // "number"
typeName [1,2];                // "array"
typeName nil;                  // "nothing"

isNil _undefinedVar;           // true (if variable doesn't exist)
isNil "myGlobal";              // true if global myGlobal doesn't exist
isNil nil;                     // true

_handle = spawn { sleep 1; };
isNull _handle;                // false (not done yet)
// ... after 1 second ...
isNull _handle;                // true (resolved)
```

---

## 10. HashMap Commands

| Command | Arity | Description |
|---|---|---|
| `createHashMap` | nular | Create empty hashmap. |
| `createHashMapFromArray` | unary | Create hashmap from `[key1, val1, key2, val2, ...]`. |
| `get` | binary | Get value by key from hashmap. Also works on arrays (index) and shared values. |
| `set` | binary | Set key-value. Right side = `[key, value]`. Works on hashmaps, arrays (index assignment), and shared values. |

```sqf
_map = createHashMap;
_map set ["health", 100];
_map set ["name", "player"];
_hp = _map get "health";       // 100
_name = _map get "name";       // "player"
_missing = _map get "xyz";     // nil

_map2 = createHashMapFromArray ["a", 1, "b", 2, "c", 3];
_map2 get "b";                 // 2
```

---

## 11. Code Execution Commands

These are compiler-level constructs, not registered commands. They have dedicated opcodes.

### call — Synchronous execution

```sqf
// Unary: call { code }
_result = call { 1 + 2; };            // returns 3

// Binary: args call { code }
_result = [10, 20] call {
    params ["_a", "_b"];
    _a + _b                            // 30
};

// Prefix call with code variable
_myFunc = compile "(_this select 0) * 2";
_result = 5 call _myFunc;              // 10
```

`call` inherits the caller's scheduler. Cannot suspend (no `sleep`/`await` inside).

### spawn — Asynchronous execution

```sqf
// Unary: spawn { code }
_handle = spawn {
    sleep 5;
    systemChat "done";
};

// Binary: args spawn { code }
[100] spawn {
    params ["_val"];
    sleep 1;
    systemChat f"Got {_val}";
};
```

Returns `ScriptHandle`. Code runs in new fiber on current scheduler.

### execVM — Load and spawn file

```sqf
_handle = execVM "scripts\init.sqf";
```

Unary. Reads file from disk, compiles, spawns.

### compile — Runtime compilation

```sqf
_code = compile "hint str _this;";
"hello" call _code;
```

Unary. Compiles string to `Code` value at runtime.

### callUnscheduled — Execute outside scheduler

```sqf
callUnscheduled { expensiveCalc(); };
```

Unary. Runs code outside scheduler's fiber system (used for `isNil {code}` SQF pattern). Cannot suspend.

---

## 12. Concurrency & Scheduling Commands

### Core VM-level commands

| Command | Arity | Description |
|---|---|---|
| `sleep` | unary | Suspend fiber for N seconds. Yields to scheduler. |
| `await` | unary | Wait for ScriptHandle to resolve. Fiber suspends, not thread. |
| `timeout` | binary | Race handle against timer. Returns new ScriptHandle that resolves on first completion. |
| `terminate` | unary | Force-resolve a ScriptHandle. Stops the associated fiber. |
| `scriptDone` | unary | Check if handle/promise is resolved. |
| `canSuspend` | unary | Check if current fiber runs in scheduled environment (can use sleep/await). |
| `spawnOn` | unary/binary | Spawn code on named scheduler. Unary: `spawnOn ["name", {code}]`. Binary: `args spawnOn ["name", {code}]`. |

```sqf
// Sleep
sleep 2.5;                     // suspend for 2.5 seconds

// Await
_handle = spawn { sleep 3; return 42; };
_result = await _handle;       // blocks until handle resolves, returns 42

// Timeout
_handle = spawn { sleep 10; return "slow"; };
_fast = _handle timeout 2;     // resolves after 2 seconds (nil, since original didn't finish)
scriptDone _fast;              // true after 2s
scriptDone _handle;            // still false (original still running, unless terminated)

// Terminate
terminate _handle;             // force-resolve handle immediately

// canSuspend
_can = canSuspend nil;         // true in spawned code, false in called code

// spawnOn
_handle = spawnOn ["AI", { heavyWork(); }];            // unary
[data] spawnOn ["AI", { params ["_d"]; work(_d); }];   // binary with args
```

---

## 13. Scheduler Management Commands

Registered by `DeclareSchedulerCommands()` (called automatically by host).

| Command | Arity | Description |
|---|---|---|
| `currentScheduler` | nular | ID of current scheduler (VM-level). |
| `clientOwner` | nular | Same as `currentScheduler` (SQF compat name). |
| `allSchedulers` | nular | Array of all scheduler IDs. |
| `schedulerName` | unary | Name string for given scheduler ID. |
| `schedulerExists` | unary | Boolean: does scheduler ID exist? |
| `schedulerBudget` | unary | Time budget in ms for given scheduler ID. |
| `setSchedulerBudget` | binary | Set time budget. Left = scheduler ID, right = ms. Min 0.1ms. |
| `fiberCount` | unary | Total fiber count (ready + waiting) for scheduler ID. |
| `readyFiberCount` | unary | Ready fiber count for scheduler ID. |
| `waitingFiberCount` | unary | Waiting (sleeping/awaiting) fiber count for scheduler ID. |
| `schedulerLoad` | unary | Estimated load percentage (0-100) for scheduler ID. |
| `sendTo` | binary | Transfer array ownership to another scheduler. |

```sqf
_myId = currentScheduler;           // e.g., 1
allSchedulers;                      // [1, 2, 3]
schedulerName 1;                    // "Main"
schedulerExists 99;                 // false

_budget = schedulerBudget 1;        // e.g., 3.0 (ms)
1 setSchedulerBudget 5;             // change Main budget to 5ms

fiberCount 1;                       // e.g., 12
readyFiberCount 1;                  // e.g., 3
waitingFiberCount 1;                // e.g., 9
schedulerLoad 1;                    // e.g., 45.0 (45%)

// Transfer array to scheduler 2
_arr = [1, 2, 3];
_arr sendTo 2;                      // returns new array owned by scheduler 2
```

---

## 14. Thread Safety Commands

SQ# enforces ownership: each array/hashmap belongs to one scheduler. Cross-scheduler access requires freeze/thaw, channels, or shared values.

### Ownership

| Command | Arity | Description |
|---|---|---|
| `scheduler` | unary | Owner scheduler ID of value. -1 if not owned (e.g., frozen, primitive). |
| `isSchedulerLocal` | unary | Boolean: does value belong to current scheduler? Frozen → always true. |

```sqf
_arr = [1, 2, 3];
scheduler _arr;                // returns current scheduler ID
isSchedulerLocal _arr;         // true

_frozen = freeze _arr;
scheduler _frozen;             // -1 (no owner — immutable)
isSchedulerLocal _frozen;      // true (readable from any scheduler)
```

### Shared (Atomic)

| Command | Arity | Description |
|---|---|---|
| `shared` | (declaration) | Declare CAS-based atomic variable. Like `private` but thread-safe. |
| `add` | binary | Atomic add. If left is Shared: atomic increment. Otherwise: regular add. |
| `sub` | binary | Atomic subtract. If left is Shared: atomic decrement. Otherwise: regular sub. |
| `get` | unary/binary | Unary: get Shared value. Binary: get from hashmap/array/shared. |
| `set` | binary | Set Shared value or hashmap/array element. |
| `compareSwap` | binary | Atomic compare-and-swap. Left = shared, right = `[expected, newValue]`. Returns `true` if swap succeeded. |

```sqf
shared _counter = 0;
_counter add 1;                // atomic increment
_val = get _counter;           // read current value
_counter sub 2;                // atomic decrement

// CAS loop
_old = get _counter;
_success = _counter compareSwap [_old, _old + 1];
// _success is true if swap happened, false if another fiber changed value
```

---

## 15. Error Handling Commands

| Command | Arity | Description |
|---|---|---|
| `throw` | unary | Throw error. If inside `try` block, jumps to `catch`. Otherwise, fiber error. |

```sqf
try {
    if (_hp <= 0) then { throw "dead"; };
    doStuff();
} catch (_error) {
    systemChat f"Error: {_error}";   // _error is SqError with .message, .file, .line, .col
};
```

`try`/`catch` are compiler constructs (not registered commands). Inside `catch` block, magic variable `_exception` holds the caught error.

---

## 16. Output Commands

### Core (always available)

| Command | Arity | Description |
|---|---|---|
| `print` | unary | Generic output. Fires host `OnPrint` event. |

### Arma Compat (opt-in via `DeclareArmaCompatCommands()`)

Registered by default in `SqHost`. Fire `OnPrint` with different prefixes.

| Command | Arity | Description |
|---|---|---|
| `hint` | unary | On-screen hint. |
| `systemChat` | unary | System chat message. |
| `diag_log` | unary | Diagnostic log (prefixed with `[DIAG]`). |

```sqf
print "Hello world";
hint "Mission started";
systemChat "Player joined";
diag_log "Debug value";
```

---

## 17. Time Commands

| Command | Arity | Description |
|---|---|---|
| `diag_tickTime` | nular | Current scheduler time in seconds (double). Monotonic, not wall-clock. |

```sqf
_start = diag_tickTime;
sleep 1;
_elapsed = diag_tickTime - _start;  // ~1.0
```

---

## 18. Multiplayer Commands

Registered by `DeclareMultiplayerCommands()`. **NOT called automatically** — host must invoke explicitly.

### Identity

| Command | Arity | Description |
|---|---|---|
| `isServer` | nular | Boolean: is this process the server? (Always true in SQ# host.) |
| `isDedicated` | nular | Boolean: is this a dedicated server (no UI)? Host sets `IsDedicated`. |
| `hasInterface` | nular | Boolean: does this host have player UI? Host sets `HasInterface`. |
| `isClient` | nular | Inverse of `isServer`. |
| `player` | nular | Placeholder: returns player ID (1 if HasInterface, else 0). |
| `allPlayers` | nular | Placeholder: array of player IDs. |

### Remote Execution

| Command | Arity | Description |
|---|---|---|
| `remoteExec` | binary | Remote execute. Left = args, right = `[commandName, target, isJip]`. target: 0=everyone, 2=server, -2=except server, clientId=specific. |
| `remoteExecCall` | binary | Same as `remoteExec` but uses `call` instead of `spawn`. |

### Public Variables

| Command | Arity | Description |
|---|---|---|
| `publicVariable` | unary | Broadcast variable name to all clients. |
| `publicVariableServer` | unary | Send variable to server only. |
| `publicVariableClient` | binary | Send variable to specific client. Left = varName, right = clientId. |

### Network Info

| Command | Arity | Description |
|---|---|---|
| `owner` | unary | Placeholder: object owner (hardware machine ID). Always returns 2.0. |
| `netId` | unary | Placeholder: network ID string. Always returns `"0:0"`. |
| `objectFromNetId` | unary | Placeholder: object from net ID. Always returns nil. |
| `didJIP` | nular | Placeholder: did join in progress? Always false. |
| `didJIPOwner` | nular | Placeholder: did JIP owner? Always false. |

```sqf
if (isServer) then {
    // server-only logic
};

// Remote execution (placeholder — full impl in network layer)
[params] remoteExec ["fn_process", 0, false];  // execute everywhere
[params] remoteExec ["fn_process", 2, false];  // execute on server only

// Public variable
myVar = 42;
publicVariable "myVar";      // broadcast to all clients
```

> **Note**: Multiplayer commands are **stubs**. Full networking (UDP/TCP, serialization, client tracking) is host responsibility. See `docs/multiplayer.md`.

---

## 19. The `params` Command

`params` is a **compiler-level** construct, not a runtime-registered command. It destructures an array into named local variables.

```sqf
_arr = [42, "hello", true];
_arr params ["_num", "_str", "_flag"];
// _num = 42, _str = "hello", _flag = true

// With defaults:
_arr params ["_num", ["_opt", 99]];
// If _arr has < 2 elements, _opt = 99

// Type-checked:
_arr params [["_num", "number"], ["_name", "string"]];
// Throws if types don't match
```

The compiler inlines `params` into `_this select N` with type checks. No-op at runtime if not present.

---

## 20. Arithmetic Operators (VM Built-ins)

These are the low-level VM built-in overrides. The host re-registers many with additional features (string concat, Shared unwrap). Listed for completeness.

| Command | Arity | VM Behavior |
|---|---|---|
| `+` | binary | Pure number add (no string concat). |
| `-` | binary | Pure number subtract. |
| `*` | binary | Pure number multiply. |
| `/` | binary | Pure number divide. Zero divisor → error. |
| `%` | binary | Pure number modulo. Zero divisor → error. |
| `==` | binary | Equality. Shared-aware compare. |
| `!=` | binary | Inequality. Shared-aware compare. |
| `<` | binary | Less than. |
| `>` | binary | Greater than. |
| `<=` | binary | Less or equal. |
| `>=` | binary | Greater or equal. |
| `!` | unary | Logical NOT. |
| `pushBack` | binary | Array push back. |
| `select` | binary | Array/string element. |
| `forEach` | binary | Array iteration. |
| `callUnscheduled` | unary | Execute code outside scheduler. |
| `sleep` | unary | Fiber sleep. |
| `count` | unary | Array/string length. |
| `add` | binary | Shared-aware add. |
| `sub` | binary | Shared-aware subtract. |
| `get` | unary | Shared get. |
| `set` | binary | Shared set only (VM-level). |
| `compareSwap` | binary | Shared CAS. |
| `clientOwner` | nular | Scheduler ID. |
| `currentScheduler` | nular | Scheduler ID. |
| `scheduler` | unary | Value owner scheduler. |
| `isSchedulerLocal` | unary | Value ownership check. |
| `canSuspend` | unary | Can fiber suspend? |
| `throw` | unary | Throw error. |
| `isNil` | unary | Nil/undefined check. |
| `spawnOn` | unary/binary | Cross-scheduler spawn. |
| `await` | unary | Handle await. |
| `timeout` | binary | Handle timeout. |
| `terminate` | unary | Fiber termination. |
| `scriptDone` | unary | Handle resolution check. |

> **Note**: Host re-registers arithmetic, comparison, array, and shared commands with enhanced behavior. The host versions take precedence. This table is for understanding the VM layer; prefer using the host-level commands documented above.

---

## Quick Index (Alphabetical)

| Command | Arity | Category |
|---|---|---|
| `!` | unary | Logic |
| `!=` | binary | Comparison |
| `%` | binary | Arithmetic |
| `&&` | binary | Logic |
| `*` | binary | Arithmetic |
| `+` | binary | Arithmetic / String |
| `-` | unary/binary | Arithmetic |
| `/` | binary | Arithmetic |
| `<` | binary | Comparison |
| `<=` | binary | Comparison |
| `==` | binary | Comparison |
| `>` | binary | Comparison |
| `>=` | binary | Comparison |
| `^` | binary | Arithmetic (power) |
| `\|\|` | binary | Logic |
| `abs` | unary | Math |
| `acos` | unary | Math |
| `add` | binary | Shared / Arithmetic |
| `allPlayers` | nular | Multiplayer |
| `allSchedulers` | nular | Scheduler |
| `append` | binary | Array |
| `asin` | unary | Math |
| `atan2` | binary | Math |
| `await` | unary | Concurrency |
| `callUnscheduled` | unary | Code Execution |
| `canSuspend` | unary | Concurrency |
| `ceil` | unary | Math |
| `clientOwner` | nular | Scheduler |
| `compareSwap` | binary | Shared |
| `compile` | unary | Code Execution |
| `cos` | unary | Math |
| `count` | unary | Array / String |
| `createHashMap` | nular | HashMap |
| `createHashMapFromArray` | unary | HashMap |
| `currentScheduler` | nular | Scheduler |
| `deleteAt` | binary | Array |
| `deleteRange` | binary | Array |
| `diag_log` | unary | Output |
| `diag_tickTime` | nular | Time |
| `didJIP` | nular | Multiplayer |
| `didJIPOwner` | nular | Multiplayer |
| `execVM` | unary | Code Execution |
| `exp` | unary | Math |
| `false` | nular | Value |
| `fiberCount` | unary | Scheduler |
| `find` | binary | Array / String |
| `floor` | unary | Math |
| `forEach` | binary | Array |
| `format` | unary | String |
| `freeze` | unary | Thread Safety |
| `get` | unary/binary | Shared / HashMap |
| `hasInterface` | nular | Multiplayer |
| `hint` | unary | Output |
| `in` | binary | Array / String |
| `isClient` | nular | Multiplayer |
| `isDedicated` | nular | Multiplayer |
| `isFrozen` | unary | Thread Safety |
| `isNil` | unary | Type |
| `isNull` | unary | Type |
| `isSchedulerLocal` | unary | Thread Safety |
| `isServer` | nular | Multiplayer |
| `joinString` | binary | String |
| `log` | unary | Math |
| `max` | binary | Arithmetic |
| `min` | binary | Arithmetic |
| `netId` | unary | Multiplayer |
| `nil` | nular | Value |
| `objectFromNetId` | unary | Multiplayer |
| `owner` | unary | Multiplayer |
| `parseNumber` | unary | String |
| `player` | nular | Multiplayer |
| `pow` | binary | Math |
| `print` | unary | Output |
| `publicVariable` | unary | Multiplayer |
| `publicVariableClient` | binary | Multiplayer |
| `publicVariableServer` | unary | Multiplayer |
| `pushBack` | binary | Array |
| `random` | unary | Random |
| `readyFiberCount` | unary | Scheduler |
| `remoteExec` | binary | Multiplayer |
| `remoteExecCall` | binary | Multiplayer |
| `resize` | binary | Array |
| `reverse` | unary | Array |
| `round` | unary | Math |
| `scheduler` | unary | Thread Safety |
| `schedulerBudget` | unary | Scheduler |
| `schedulerExists` | unary | Scheduler |
| `schedulerLoad` | unary | Scheduler |
| `schedulerName` | unary | Scheduler |
| `scriptDone` | unary | Concurrency |
| `select` | binary | Array / String |
| `selectRandom` | unary | Random |
| `selectRandomWeighted` | unary | Random |
| `sendTo` | binary | Scheduler / Thread Safety |
| `set` | binary | Shared / Array / HashMap |
| `setSchedulerBudget` | binary | Scheduler |
| `sin` | unary | Math |
| `sleep` | unary | Concurrency |
| `sort` | binary | Array |
| `spawnOn` | unary/binary | Concurrency |
| `splitString` | binary | String |
| `sqrt` | unary | Math |
| `str` | unary | String / Type |
| `sub` | binary | Shared / Arithmetic |
| `systemChat` | unary | Output |
| `tan` | unary | Math |
| `terminate` | unary | Concurrency |
| `thaw` | unary | Thread Safety |
| `throw` | unary | Error |
| `timeout` | binary | Concurrency |
| `toArray` | unary | String |
| `toLower` | unary | String |
| `toString` | unary | String |
| `toUpper` | unary | String |
| `trim` | unary | String |
| `true` | nular | Value |
| `typeName` | unary | Type |
| `waitingFiberCount` | unary | Scheduler |

---

## See Also

- [Quick Reference](quick-reference.md) — syntax, control flow, magic variables
- [Language Specification](language-spec.md) — operator precedence, arity rules
- [Types](types.md) — type system overview
- [Multi-Threading](multithreading.md) — freeze/thaw, channels, shared, ownership
- [Multiplayer](multiplayer.md) — networking architecture, host responsibilities
- [For SQF Scripters](for-sqf-scripters.md) — differences from Arma 3 SQF

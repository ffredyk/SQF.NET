---
description: "SQ# command design rules — naming, arity, parameter placement. Apply when registering, implementing, documenting, or using SQ# commands."
applyTo: "src/**/*.cs, samples/**/*.sqf, docs/**/*.md"
---

# SQ# Command Design Rules

Rules for naming, arity, and parameter placement of SQ# scripting commands.
Apply when registering commands in C#, writing .sqf scripts, or documenting the language.

---

## Rule 1: Command Naming — camelCase, Simple, Coherent

Commands use **camelCase**. Names are short, meaningful, and follow consistent patterns.
Avoid redundant prefixes like `create` when a simpler name or keyword already conveys the meaning.

### ✅ Good names

| Command | Why |
|---|---|
| `pushBack` | Action verb — clear what it does |
| `deleteAt` | Action + preposition — deletes AT index |
| `deleteRange` | Action + noun — deletes a range |
| `isFrozen` | `is` prefix — boolean query |
| `canSuspend` | `can` prefix — capability query |
| `scriptDone` | Noun + adjective — state check |
| `currentScheduler` | Adjective + noun — nular getter |
| `compareSwap` | Verb + verb — CAS operation |
| `splitString` | Verb + noun — what it operates on |
| `joinString` | Verb + noun — what it operates on |
| `spawnOn` | Verb + preposition — spawn ON target |

### ❌ Bad names (and why)

| Bad | Problem | Better |
|---|---|---|
| `createChannel` | `channel` keyword already exists — redundant `create` | `channel` (keyword) |
| `CreateHashMap` | PascalCase — SQ# commands are camelCase | `createHashMap` |
| `get_player_count` | snake_case — not SQ# style | `countPlayers` |
| `doTheThing` | Vague — commands should say what they do | specific verb+noun |
| `arrPushBack` | Type prefix — types inferred from context | `pushBack` |
| `spawn_on` | snake_case | `spawnOn` |

### Naming patterns by category

| Category | Pattern | Examples |
|---|---|---|
| **Action** (mutates) | `verbNoun` or `verbPreposition` | `pushBack`, `deleteAt`, `resize`, `freeze`, `thaw` |
| **Boolean query** | `isAdjective` / `canVerb` / `hasNoun` | `isFrozen`, `isNil`, `canSuspend`, `canReceive` |
| **Getter** (nular) | `adjectiveNoun` or `nounNoun` | `currentScheduler`, `clientOwner` |
| **Conversion** | `toType` / `fromType` / `parseType` | `toString`, `toArray`, `parseNumber` |
| **Constructor** (nular) | `createType` (only when no keyword exists) | `createHashMap`, `createNamespace` |
| **Keyword** (declaration) | bare lowercase word | `private`, `global`, `shared`, `channel` |

---

## Rule 2: Arity — Exactly Three Forms

Every SQ# command has exactly ONE of three arities. No exceptions.

### Nular — Zero Parameters

Just the command name. Returns a value. Like a property access.

```
commandName
```

| Examples | Returns |
|---|---|
| `nil` | Nothing value |
| `player` | Current player object (host-defined) |
| `currentScheduler` | Scheduler ID |
| `clientOwner` | Client owner ID |
| `canSuspend` | Boolean — can current fiber suspend? |
| `createHashMap` | New empty HashMap |

**C# registration:**
```csharp
host.RegisterNular("commandName", () => /* compute and return SqValue */);
```

### Unary — One Parameter on RIGHT

Command consumes exactly ONE expression on its right side.

```
commandName _rightExpression
```

| Examples | Right side |
|---|---|
| `count _arr` | Array → Number |
| `sleep 1.5` | Number (seconds) |
| `freeze _arr` | Array → FrozenArray |
| `str _val` | Any → String |
| `hint "hello"` | String |
| `receive _channel` | Channel → received value |
| `canReceive _channel` | Channel → Boolean |

**C# registration:**
```csharp
host.RegisterUnary("commandName", arg => /* process arg, return SqValue */);
```

### Binary — Left Parameter AND Right Parameter

Command takes a left-side value AND consumes exactly ONE expression on its right side.

```
_leftValue commandName _rightExpression
```

| Examples | Left | Right |
|---|---|---|
| `_arr pushBack 5` | Array | Value to append |
| `_arr select 0` | Array | Index |
| `_map get "key"` | HashMap | Key |
| `_ch send _data` | Channel | Data to send |
| `_handle continueWith {code}` | ScriptHandle | Code block |
| `a + b` | Number | Number |

**C# registration:**
```csharp
host.RegisterBinary("commandName", (left, right) => /* process, return SqValue */, precedence: 4);
```

---

## Rule 3: Multi-Parameter Commands (>2 Total) — Array on Right

When a command needs more than TWO total parameters (left + right = 3+), the extra parameters
go into an **array on the right side**. The left side (if any) is a single value.

### Binary form with array right (3+ total params)

Left side = one value. Right side = `[param1, param2, ...]` array.

```
_leftValue commandName [param1, param2, param3]
```

| Examples | Left | Right array |
|---|---|---|
| `_arr set [idx, val]` | Array | `[index, value]` |
| `_arr deleteRange [start, count]` | Array | `[startIndex, count]` |
| `_arr param [index, defaultValue]` | Array | `[index, default]` |
| `_counter compareSwap [expected, new]` | Shared | `[expectedValue, newValue]` |
| `_args spawnOn ["AI", {code}]` | Arguments | `[schedulerName, code]` |

**C# registration:**
```csharp
host.RegisterBinary("set", (left, right) =>
{
    // left = array, right = [index, value] array
    var args = right.AsArray();
    var index = (int)args[0].AsNumber();
    var value = args[1];
    left.AsArray()[index] = value;
    return SqValue.Nil;
}, precedence: 4);
```

### Unary form with array right (2+ params, no left side)

No left value. Right side = `[param1, param2, ...]` array.

```
commandName [param1, param2, param3]
```

| Examples | Right array |
|---|---|
| `spawnOn ["AI", {code}]` | `[schedulerName, code]` |
| `continueWithOn ["Main", {code}]` | `[schedulerName, code]` |
| `PromiseAll [_h1, _h2, _h3]` | `[handle1, handle2, handle3]` |

**C# registration:**
```csharp
host.RegisterUnary("spawnOn", arg =>
{
    // arg = [schedulerName, code]
    var args = arg.AsArray();
    var schedulerName = args[0].AsString();
    var code = args[1].AsCode();
    // ... spawn on named scheduler
});
```

### Anti-pattern: Multiple Right-Side Values (PARSER REJECTS)

```
// ❌ WRONG — TWO right-side expressions (scheduler name + code block):
spawnOn "AI" { code };

// ✅ CORRECT — array wraps them into ONE right-side expression:
spawnOn ["AI", { code }];

// ❌ WRONG — THREE right-side expressions:
continueWithOn "Main" { code };

// ✅ CORRECT:
continueWithOn ["Main", { code }];
```

---

## Rule 4: Declaration Keywords Are NOT Commands

`private`, `global`, `shared`, `channel` are **declaration keywords** — not nular/unary/binary commands.
They follow `keyword _name = value` pattern (C#-like), not SQF operator chaining.

```sqf
// ✅ Keywords — declaration syntax:
private _x = 5;
global CONFIG = 42;
shared _counter = 0;
channel _pipe;

// ❌ NOT commands — don't use SQF operator patterns:
// private _x = 5;  ← correct as keyword, but NOT parsed as "private(_x = 5)"
// Shared create 0; ← wrong, use keyword: shared _counter = 0;
// Channel create;  ← wrong, use keyword: channel _pipe;
```

For types that DON'T have a keyword, use nular creation commands:
```sqf
// ✅ Nular creation commands (no keyword exists):
private _map = createHashMap;
private _ns = createNamespace "MyNS";
```

---

## Rule 5: Precedence for Binary Commands

Binary commands need a precedence level. Default is **4** (PrecBinary) unless the command
is arithmetic (6-7), comparison (3), or logical (1-2).

| Precedence | Category | Example commands |
|---|---|---|
| 7 | Mul/Div | `*`, `/`, `%`, `mod` |
| 6 | Add/Sub | `+`, `-`, `min`, `max` |
| 4 | **General binary (DEFAULT)** | `pushBack`, `select`, `set`, `resize`, `spawnOn`, `send` |
| 3 | Comparison | `==`, `!=`, `<`, `>`, `<=`, `>=` |
| 2 | Logical AND | `&&`, `and` |
| 1 | Logical OR | `\|\|`, `or` |

```csharp
// Default precedence for most binary commands:
host.RegisterBinary("send", (ch, data) => { ... }, precedence: 4);

// Arithmetic commands get higher precedence:
host.RegisterBinary("addScore", (a, b) => new SqValue(a.AsNumber() + b.AsNumber()), precedence: 6);
```

---

## Quick Audit Checklist

When adding or reviewing a command, verify:

- [ ] **Name is camelCase** — not PascalCase, not snake_case
- [ ] **Name is simple** — no redundant prefixes (no `create` when keyword exists, no type prefixes)
- [ ] **Name fits category pattern** — boolean queries use `is`/`can`/`has`, actions use verbNoun, etc.
- [ ] **Arity is exactly one** — nular, unary, or binary. Not a mix.
- [ ] **Unary: one right-side parameter** — `command _right`
- [ ] **Binary: left + one right-side parameter** — `_left command _right`
- [ ] **3+ total params: array on right** — `_left command [a, b]` or `command [a, b]`
- [ ] **Never multiple right-side expressions** — always wrap multi-values in `[...]`
- [ ] **Keywords use declaration syntax** — `channel _ch`, not `Channel create`
- [ ] **Precedence is correct** — default 4, arithmetic 6-7, comparison 3, logic 1-2
- [ ] **C# handler signature matches arity** — `Func<SqValue>` for nular, `Func<SqValue, SqValue>` for unary, `Func<SqValue, SqValue, SqValue>` for binary

---

## Examples: Correct vs Wrong

```sqf
// === NULAR ===
// ✅ Correct:
nil
player
currentScheduler
createHashMap

// ❌ Wrong:
getPlayer()            // No parens in SQF
currentScheduler()     // No parens


// === UNARY ===
// ✅ Correct:
count _arr
sleep 2.5
freeze _data
hint "hello"
receive _channel
canReceive _channel
spawnParallel { code }

// ❌ Wrong:
sleep(2.5)             // No parens
count(_arr)            // No parens
freeze _data _extra    // TWO right values


// === BINARY (two params total) ===
// ✅ Correct:
_arr pushBack 42
_arr select 0
_map get "key"
_ch send _data
_handle continueWith { code }
_args spawn { code }
a + b

// ❌ Wrong:
_arr pushBack (42)     // Unnecessary parens (not wrong, just redundant)
pushBack _arr 42       // Binary commands have left side first


// === BINARY WITH ARRAY RIGHT (3+ params total) ===
// ✅ Correct:
_arr set [0, 99]
_arr deleteRange [2, 5]
_counter compareSwap [42, 99]
_args spawnOn ["AI", { code }]
_handle continueWithOn ["Main", { code }]

// ❌ Wrong:
_arr set 0 99                  // THREE separate expressions — must use array
_args spawnOn "AI" { code }    // TWO right values — must use array
spawnOn "AI" { code }          // TWO right values — must use array


// === UNARY WITH ARRAY RIGHT (2+ params, no left side) ===
// ✅ Correct:
spawnOn ["AI", { code }]
continueWithOn ["Main", { code }]
PromiseAll [_h1, _h2, _h3]

// ❌ Wrong:
spawnOn "AI" { code }          // TWO right values
PromiseAll _h1 _h2 _h3         // THREE separate expressions


// === KEYWORDS ===
// ✅ Correct:
private _x = 5;
global SCORE = 0;
shared _counter = 0;
channel _events;

// ❌ Wrong:
Channel create;                  // Not SQF style — use channel keyword
Shared create 0;                 // Not SQF style — use shared keyword
private _counter = Shared create 0;  // Mixed old/new — use shared keyword
```

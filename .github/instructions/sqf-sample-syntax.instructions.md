---
description: "SQ# sample syntax rules — strict arity, common mistakes, command forms. Applies to all .sqf sample files."
applyTo: "samples/**/*.sqf"
---

# SQ# Sample Syntax Rules

These rules prevent common mistakes when writing SQ# script samples and documentation.

## Rule 1: Strict Arity — One Right-Side Expression Only

**Every SQ# command takes exactly ONE expression on its right side.**

If a command needs 2+ parameters, they MUST be wrapped in an array `[...]`.
This is the fundamental design of SQF/SQ# and the parser enforces it.

```sqf
// ✅ CORRECT — one right-side expression:
count _arr                         // unary: right = _arr
_arr select 0                      // binary: left = _arr, right = 0
hint "hello"                       // unary: right = "hello"
_arr set [0, 99]                   // binary: left = _arr, right = [0, 99] (array!)
spawn { code }                     // unary: right = { code }
_args spawn { code }               // binary: left = _args, right = { code }

// ❌ WRONG — multiple right-side expressions:
spawnOn "AI" { code }              // TWO right values: "AI" and { code } → must wrap
continueWithOn "Main" { code }     // TWO right values → must wrap
```

## Rule 2: Multi-Parameter Commands MUST Use Array

Commands that take multiple parameters wrap them in `[...]` on the right side.

### spawnOn (SQ# addition)

Takes scheduler name + code. Always wraps in array:

```sqf
// ✅ CORRECT — unary form (no arguments):
spawnOn ["AI", {
    heavyComputation();
    return result;
}];

// ✅ CORRECT — binary form (pass arguments on left):
_args spawnOn ["AI", {
    params ["_data"];
    process(_data);
    return "done";
}];

// ❌ WRONG — never do this:
spawnOn "AI" { code };             // TWO right values
_args spawnOn "AI" { code };       // TWO right values
```

### continueWithOn (SQ# addition)

Takes scheduler name + code. Always wraps in array:

```sqf
// ✅ CORRECT:
_handle continueWithOn ["Main", {
    updateUI(_this);
}];

// ❌ WRONG:
_handle continueWithOn "Main" { updateUI(_this); };
```

### Other multi-param commands (all same pattern)

```sqf
// ✅ CORRECT — array on right:
_arr set [index, value]
_arr deleteRange [start, count]
_arr param [index, defaultValue]
_arr insert [index, value]

// Terminal form (no left side):
set [0, 99]                        // valid but unusual — same array-on-right rule
```

## Rule 3: Single-Parameter Commands Are Fine Without Array

If a command truly takes only ONE parameter (beyond the left side), no array needed:

```sqf
// ✅ CORRECT — unary (1 param on right):
hint "hello"                       // right = "hello"
str 42                             // right = 42
count _arr                         // right = _arr
spawn { code }                     // right = { code }
spawnParallel { code }             // right = { code }
sleep 1.5                          // right = 1.5

// ✅ CORRECT — binary (left + 1 right):
_args call _function               // right = _function (single code value)
_arr pushBack 42                   // right = 42 (single value)
_handle continueWith { code }      // right = { code } (single code value)
_handle terminate "value"          // right = "value" (single value)
_handle onProgress { code }        // right = { code } (single code value)
_arr apply { _x * 2 }              // right = { code } (single code value)
_arr select { _x > 0 }             // right = { code } (single code value)
```

## Rule 4: Declaration Keywords — private, global, shared, channel

SQ# has four declaration keywords. They follow the same syntactic pattern:

```sqf
// ✅ CORRECT — declaration keywords:
private _x = 5;       // local variable, optional initializer
global CONFIG = 42;   // global variable (scheduler-local)
shared _counter = 0;  // CAS-based atomic variable, must have initializer
channel _pipe;         // lock-free message channel, NO initializer needed
```

### channel keyword (SQ# addition)

`channel` declares and creates a new message channel. Like `private`/`shared`, it's a keyword — NOT a `Channel create` two-token expression.

```sqf
// ✅ CORRECT:
channel _ch;                          // keyword — creates new channel

// Operations on channels:
_ch send _data;                       // binary:  left=channel, right=data to send
private _val = receive _ch;           // unary:   right=channel, returns received value
if (canReceive _ch) then { ... };     // unary:   right=channel, returns bool
```

```sqf
// ❌ WRONG — never use two-token "Channel create":
private _ch = Channel create;         // NOT valid SQ# — use channel keyword
Channel send _ch _data;               // NOT valid — send is binary on channel value
```

### shared keyword (SQ# addition)

```sqf
// ✅ CORRECT:
shared _counter = 0;                  // keyword — creates Shared<Number> with initial value
_counter add 1;                       // binary: left=shared, right=amount (atomic increment)
_counter get;                         // binary: left=shared, right=nothing → returns value
_counter compareSwap [42, 99];        // binary: left=shared, right=[expected, new] → bool
_counter set 100;                     // binary: left=shared, right=new value (atomic write)
```

### Why keywords, not two-token "Type action"?

SQF uses `createHashMap`, `createVehicle` — single-token nular commands for creation. SQ# extends this with declaration keywords (`private`, `global`, `shared`, `channel`) for better readability and to avoid ambiguous `Type verb` parsing.

```sqf
// ✅ SQ# style — keyword declarations:
private _arr = [1, 2, 3];
global MAX_PLAYERS = 64;
shared _score = 0;
channel _events;

// Also ✅ — nular creation commands for types without keywords:
private _map = createHashMap;
private _ns = createNamespace "MyNS";
```

### Promise combinators

```sqf
// ✅ CORRECT — unary, right = array of handles:
private _results = PromiseAll [_h1, _h2, _h3];
private _winner = PromiseRace [_h1, _h2];
private _first = PromiseAny [_h1, _h2];
```

## Rule 5: Check Against Language Spec

When in doubt, consult `docs/language-spec.md` section "Strict Arity Rule":

> Every command takes exactly **ONE expression** on its right side. Commands needing
> 3+ parameters MUST use an **array** on the right side. No exceptions.

And the examples in the spec:

```sqf
// Correct:
spawnOn ["AI", { code }]      // unary: right = [scheduler, code]
_args spawnOn ["AI", { ... }] // binary: left = args, right = [scheduler, code]

// Wrong (parser rejects):
spawnOn "AI" { code }         // TWO right-side values — must wrap in array
```

## Quick Checklist Before Committing .sqf Samples

- [ ] Every command has exactly ONE right-side expression
- [ ] Multi-param commands use `[param1, param2, ...]` array on right
- [ ] `spawnOn` always uses array: `spawnOn ["Name", { code }]`
- [ ] `continueWithOn` always uses array: `continueWithOn ["Name", { code }]`
- [ ] Single-param commands (spawn, call, sleep, pushBack, apply, select, count) are fine without array
- [ ] Promise combinators take array of handles
- [ ] Binary commands that need 2 right params use array: `_arr set [idx, val]`, `_arr deleteRange [start, n]`

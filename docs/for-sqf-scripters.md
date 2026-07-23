# SQ# for SQF Scripters

> What changed, what stayed, and how to write SQ# scripts coming from Arma 3 SQF.

## The Short Version

SQ# **is** SQF with fixes. If you know SQF, you know SQ#. The differences are
mostly things that annoyed you about SQF — we fixed them.

```sqf
// SQF (Arma 3):                     // SQ# (same thing):
_count = 0;                          private _count = 0;
_arr = [1,2,3];                      private _arr = [1, 2, 3];
_arr pushBack 4;                     _arr pushBack 4;
if (alive player) then { ... };      if (alive player) { ... };
_count = count _arr;                 _count = count _arr;
```

---

## What Changed

### ℹ️ nil Deletes Variables (SQF Behavior Preserved)

In SQF, `_var = nil` **deletes** the variable. SQ# keeps this behavior.
`nil` is still a valid value for array elements, function returns, and comparisons — just not storable in variables.

```sqf
// SQF — works the same in SQ#:
_myVar = nil;       // Variable DELETED. _myVar no longer exists.
isNil "_myVar";     // true (string form — looks up by name)
isNil _myVar;       // true (variable form — compile-time check)

// nil as a value still works:
private _arr = [1, nil, 3];    // nil in arrays is fine
private _val = _arr select 1;  // nil → variable deleted
if (_arr select 1 == nil) then { ... }; // comparison works
```

### ✅ Fixed: Undefined Variables Are Errors

```sqf
// SQF: silently does nothing or crashes weirdly
_undefinedVar;      // Might work, might not, might error with "Zero Divisor"

// SQ#: clear error
_undefinedVar;      // ERROR: Undefined variable '_undefinedVar'
```

### ℹ️ Arma Output Commands Are Opt-In

`hint`, `systemChat`, `diag_log` are Arma-specific. Your host may not include them.
SQ# provides a generic `print` command that always works:

```sqf
// Always available:
print "Hello World";          // → host.OnPrint

// Arma compat (host must opt-in):
hint "Hello World";           // same as print, Arma naming
systemChat "text";
diag_log "debug";
```

### ✅ Fixed: String Handling

SQF has no escape sequences. SQ# does.

```sqf
// SQF:
_str = "Line 1" + endl + "Line 2";           // clunky
_path = "C:\Users\Name\Documents";           // \U, \N, \D become Unicode escapes — bug!

// SQ#:
_str = "Line 1\nLine 2";                     // \n works
_path = @"C:\Users\Name\Documents";          // verbatim string — no escapes
_multiline = """
    Line 1
    Line 2
    """;                                      // multi-line
```

### 🆕 NEW: String Interpolation

```sqf
// SQF:
_msg = format ["Player %1 has %2 HP", _name, _hp];

// SQ#:
_msg = f"Player {_name} has {_hp} HP";       // cleaner, type-checked
```

### ✅ Fixed: `==` Is Strict

In SQF, `==` does weird type coercion. `"123" == 123` might work or error depending on context.
In SQ#, `==` is always strict (like SQF's `isEqualTo`).

```sqf
// SQF:
"hello" == "HELLO";     // true (case-insensitive — WHAT?)
[1,2] == [1,2];         // ERROR (can't compare arrays with ==)

// SQ#:
"hello" == "HELLO";     // false (strict, case-sensitive)
[1,2] == [1,2];         // true (deep comparison works)
nil == nil;             // true
nil == anything;        // false
```

### ✅ Fixed: Array Index Out of Bounds

```sqf
// SQF:
_arr = [1,2,3];
_arr select -1;    // Error Zero Divisor — confusing error
_arr select 3;     // nil (exactly at count)
_arr select 4;     // Error Zero Divisor — inconsistent!

// SQ#:
_arr = [1, 2, 3];
_arr select -1;    // nil (safe)
_arr[99];          // nil (bracket syntax, safe)
_arr select 99;    // nil (safe)
```

### ✅ Fixed: Trailing Commas Allowed

```sqf
// SQF:
_arr = [1, 2, 3,];   // ERROR: Unexpected ","

// SQ#:
_arr = [1, 2, 3,];   // OK — trailing comma allowed
```

### 🆕 NEW: Type Annotations (Optional)

```sqf
// SQF:
private _count = 0;          // untyped

// SQ#:
private _count: int = 0;     // typed — catches errors early
private _name: string;
private _tags: string[] = [];
```

### 🆕 NEW: Try/Catch

```sqf
try {
    private _result = _arr select _index;
} catch (_error) {
    systemChat f"Error: {_error.message}";
};
```

### 🆕 NEW: Async/Await

```sqf
// SQF:
_handle = 0 spawn { sleep 5; return "done"; };
waitUntil { scriptDone _handle };

// SQ#:
_handle = spawn { sleep 5; return "done"; };
_result = await _handle;          // fiber suspends, thread doesn't block
_result = await _handle timeout 3; // with timeout
```

### 🆕 NEW: Multi-Threading (Opt-In)

```sqf
// spawnOn / spawn create NEW scope — parent locals NOT accessible.
// Must pass arguments explicitly.

// Unary form (no arguments):
_handle = spawnOn ["AI", {
    private _result = heavyPathfinding();
    return _result;
}];

// Binary form (pass arguments on left side):
_args spawnOn ["AI", {
    params ["_data"];
    process(_data);
    return "done";
}];

// Thread pool (pure computation, no host commands):
_handle = spawnParallel {
    params ["_input"];   // receives args from left side
    private _result = expensiveMath(_input);
    return _result;
};
```

### 🆕 NEW: Enhanced params

```sqf
// SQF:
params ["_name", ["_age", 0, [0]]];

// SQ# — type annotations:
params [_name: string, _age: int = 0, _tags: string[] = []];

// SQ# — destructuring:
params [_x, _y, _z]: Vector3;

// SQ# — rest params:
params [_first, ..._rest];
```

### ℹ️ Global Variables (Same as SQF)

Names without `_` are globals — same as SQF. `global` keyword is optional sugar for clarity.

```sqf
// Both work — same as SQF:
myVar = 5;                       // implicit global (SQF style)
global CONFIG_PATH = "data/config.json";  // explicit global (clearer intent)
```

---

## Identifier Rules

Same as SQF with one enhancement:

| Rule | SQF | SQ# |
|---|---|---|
| Characters | a-z, A-Z, 0-9, _ | a-z, A-Z, 0-9, _ + Unicode letters (modern) |
| First character | Letter or `_` | Letter or `_` |
| Cannot start with number | ❌ `1variable` | ❌ `1variable` |
| Local prefix | Must start with `_` | Same |
| Case-sensitive? | **No** — case-insensitive | **No** — same |
| Reserved words | Cannot use command names | Same |

```sqf
// Valid:
_myVar, _x1, TAG_Global, _camelCase, _přežral (Unicode OK in SQ#)

// Invalid:
1variable, _my-var, myVar (missing _ for local)
```

## What Stayed The Same

| Feature | Notes |
|---|---|
| `;` statement terminator | Same as SQF |
| `//` and `/* */` comments | Same |
| `private _var` scoping | Same |
| `if/else`, `while`, `for`, `switch` | Same syntax, cleaner sugar available |
| `call`, `spawn`, `execVM` | Same semantics. `call` inherits env, `spawn` always scheduled |
| `count`, `select`, `pushBack`, `append` | Same array commands |
| `format`, `str`, `parseNumber` | Same string commands |
| `params`, `param` | Same, with enhancements |
| `{}` code blocks | Same first-class values |
| Operator precedence | Same 11 levels |
| Nular/Unary/Binary commands | Same model. Host registers commands by arity. |

---

## Rules to Remember

1. **`call`/`spawn` take CODE, not commands.**
   ```sqf
   // WRONG:
   "text" call systemChat;    // systemChat is a command, not code
   
   // RIGHT:
   systemChat "text";          // unary command takes argument on right
   [1,2] call { (_this select 0) + (_this select 1) };  // call takes code block
   ```

2. **Multi-parameter commands use ARRAY on right side.**
   ```sqf
   // RIGHT — one expression on right:
   _arr set [index, value];           // right = [index, value] array
   spawnOn ["AI", { code }];          // right = ["AI", {code}] array
   
   // WRONG — two separate right-side things:
   spawnOn "AI" { code };
   ```

3. **`_`-prefixed variables are local. Bare names are global.**
   ```sqf
   private _myVar = 5;    // local — good practice
   myVar = 5;             // global (SQF style — OK)
   global MY_VAR = 5;     // global (explicit — clearer intent)
   ```

4. **Unary commands greedily consume the next expression.**
   ```sqf
   count _arr select 2;    // = (count _arr) select 2 — NOT count (_arr select 2)
   // count returns number, then select fails on number — same as SQF
   ```

5. **`spawn` creates NEW scope — parent locals NOT accessible.**
   ```sqf
   // WRONG:
   private _data = [1, 2, 3];
   spawn { hint str _data; };     // ERROR: _data undefined in spawned code!
   
   // RIGHT — pass as argument:
   [_data] spawn { params ["_d"]; hint str _d; };
   // or binary form:
   _data spawn { params ["_d"]; hint str _d; };
   
   // `call` runs in SAME scope — parent locals accessible:
   call { hint str _data; };      // OK
   ```

6. **Strings use `"` or `'` — both work the same.**
   ```sqf
   "hello"     // double-quoted
   'hello'     // single-quoted — identical in SQ# (no preprocessor inside)
   ```

---

## Quick Comparison Table

| Thing | SQF | SQ# |
|---|---|---|
| nil assignment | Deletes variable | Stores nil value |
| Undefined variable | Silent/weird error | Clear error |
| `==` comparison | Type-coercing, quirky | Strict (`isEqualTo` semantics) |
| Array OOB select | -1 = error, count = nil, count+1 = error | Always nil |
| Escape sequences | None | `\n`, `\t`, `\\`, `\"`, `\'`, `\uXXXX` |
| String interpolation | `format` only | `f"..."` + `format` |
| Trailing comma | Error | Allowed |
| Global variables | Implicit (`myVar = 5`) | Same. `global` keyword optional |
| Modules | `#include` | `#include` preprocessor (opt-in) |
| Multi-threading | Single scheduler | Multi-scheduler, opt-in |
| Thread safety | N/A | Automatic, implicit |
| Error handling | No try/catch (except Arma 3) | `try`/`catch` |
| Type annotations | None | Optional `: type` |
| Array bracket access | `_arr select 0` | `_arr[0]` (`select` still works) |
| Regular expressions | None | `_str =~ /pattern/` |

---

📖 See [syntax-sugar.md](syntax-sugar.md) for full list of optional cleaner syntax.

# SQ# Syntax Sugar

> Cleaner, shorter, less typing. Everything that works in SQF still works in SQ# — these are optional additions.

---

## 1. `then` Is Optional

`if` statements don't need `then`. Both forms work, same semantics.

```sqf
// SQF (required):
if (alive _unit) then { _unit setDamage 1; };

// SQ# (both OK — then is sugar):
if (alive _unit) { _unit setDamage 1; };
if (alive _unit) then { _unit setDamage 1; };
```

---

## 2. Trailing Commas Allowed

No more "Unexpected ','" errors. Works in arrays, params, any list.

```sqf
// SQF — ERROR:
private _arr = [1, 2, 3,];

// SQ# — fine:
private _arr = [1, 2, 3,];
params ["_a", "_b",];              // also OK
_arr set [0, 99,];                 // N/A — set takes array
```

---

## 3. Bracket Array Access

`_arr[0]` is shorter than `_arr select 0`. Both work.

```sqf
// SQF:
private _first = _arr select 0;
_arr set [1, 99];

// SQ# sugar:
private _first = _arr[0];
_arr[1] = 99;                      // assignment too
```

---

## 4. `_x` and `_forEachIndex` in forEach

No need to declare or `params` them — auto-bound like SQF.

```sqf
// Works out of the box:
[10, 20, 30] forEach {
    systemChat f"[{_forEachIndex}] = {_x}";
};
// Output: [0] = 10, [1] = 20, [2] = 30

// _this is also set: [_x, _forEachIndex, originalArray]
```

---

## 5. String Sugar

### Escape Sequences
```sqf
// SQF — no escapes, clunky:
private _msg = "Line 1" + endl + "Line 2";

// SQ#:
private _msg = "Line 1\nLine 2";     // \n, \t, \\, \", \', \uXXXX
```

### String Interpolation (f-strings)
```sqf
// SQF:
_msg = format ["Player %1 has %2 HP", _name, _hp];

// SQ#:
_msg = f"Player {_name} has {_hp} HP";
```

### Verbatim Strings
```sqf
// SQF — \U, \N, \D become Unicode bugs:
_path = "C:\Users\Name\Documents";

// SQ# — no escape processing:
_path = @"C:\Users\Name\Documents";
```

### Multi-Line Strings
```sqf
// SQ# only:
private _text = """
    Line one
    Line two
    Line three
    """;
```

---

## 6. Global Variables (Same as SQF)

Names without `_` are globals. `global` keyword is optional sugar for clarity.

```sqf
// Both work — choose your style:
myVar = 5;                       // implicit global (SQF style)
global CONFIG_VERSION = "1.0";   // explicit global (clearer intent)
global MAX_PLAYERS = 64;
```

---

## 7. `callUnscheduled` — Run Outside Scheduler

Replaces SQF's `isNil { code }` pattern. Execute code directly, bypassing the fiber queue.

```sqf
// SQF — weird pattern to run unscheduled:
if (isNil { _result = someCalculation; }) then { /* never true */ };

// SQ# — clear intent:
private _result = callUnscheduled { someCalculation };
```

---

## 8. Await with Timeout

Promise-style async. Wait for a spawn result, with optional timeout.

```sqf
// SQF — polling:
private _handle = [] spawn { sleep 5; "done" };
waitUntil { scriptDone _handle };

// SQ# — await:
private _result = await _handle;              // wait forever
private _result = await _handle timeout 3;    // nil after 3 seconds
```

---

## 9. Hex Literals

```sqf
// SQ# only:
private _red = 0xFF0000;       // 16711680
private _max = 0xFF;           // 255
```

---

## 10. Type Annotations (Optional)

Catch type errors early. Completely optional — dynamic typing still works.

```sqf
// SQ# only:
private _count: int = 0;
private _name: string = "default";
private _tags: string[] = [];
```

---

## 11. Try/Catch with Typed Errors

```sqf
// SQ# only:
try {
    private _x = _undefinedVar + 5;
} catch {
    params ["_error"];
    systemChat f"Caught: {_error}";
    // _error has .type, .message, .stack, .source
};
```

---

## Quick Reference

| Sugar | Instead of | Notes |
|---|---|---|
| `if (x) { }` | `if (x) then { }` | `then` optional |
| `_arr[0]` | `_arr select 0` | Bracket access + assignment |
| `_arr[0] = v` | `_arr set [0, v]` | Bracket assignment |
| `f"x={_x}"` | `format ["x=%1", _x]` | String interpolation |
| `@"C:\path"` | `"C:\\path"` | Verbatim string |
| `"""..."""` | N/A | Multi-line string |
| `\n`, `\t` | `endl`, manual | Escape sequences |
| `0xFF` | `255` | Hex literals |
| `global X = 5` | SQF implicit global | `global` keyword optional |
| `callUnscheduled { }` | `isNil { }` | Run outside scheduler |
| `await _h` | `waitUntil { scriptDone _h }` | Promise await |
| `await _h timeout 3` | Manual timer + polling | Timeout await |
| `: int`, `: string` | N/A | Optional type annotations |
| `try { } catch { }` | N/A | Structured error handling |
| `_x`, `_forEachIndex` | Manual params | Auto-bound in forEach |
| Trailing `,` in arrays | Must omit | Allowed |

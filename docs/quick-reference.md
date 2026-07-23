# SQ# Quick Reference

## Syntax

```sqf
// Comments
// line comment
/* block comment */

// Variables (identifiers: [a-zA-Z_][a-zA-Z0-9_]* + Unicode, case-insensitive)
private _local = 5;            // local variable (always starts with _)
global CONFIG = "value";       // explicit global
_myVar: int = 42;              // typed local (optional)

// Assignment (= only, no += -= etc.)
_x = 5;
_arr = [1, 2, 3];

// Terminator
_x = 5;                        // ; required (or ,)
```

## Operator Precedence (Highest → Lowest)

| Prec | Type | Examples |
|:---:|---|---|
| 11 | Nular, literals, brackets | `var`, `123`, `"str"`, `()`, `[]`, `{}` |
| 10 | Unary | `-x`, `!flag`, `count _arr`, `str val` |
| 9 | Hash-select | `_arr # index` |
| 8 | Power | `a ^ b` |
| 7 | Mul/Div/Mod | `*`, `/`, `%`, `mod` |
| 6 | Add/Sub/Min/Max | `+`, `-`, `min`, `max` |
| 5 | else | `else` |
| 4 | Binary commands | `select`, `pushBack`, `set`, `resize` |
| 3 | Comparisons | `==`, `!=`, `<`, `>`, `<=`, `>=` |
| 2 | AND | `&&`, `and` |
| 1 | OR | `\|\|`, `or` |

## Control Flow

```sqf
// if
if (alive player) { hint "alive"; } else { hint "dead"; };
if (_x > 0) then { hint "positive"; };     // then optional

// while
while { _x < 10 } do { _x = _x + 1; };

// for
for "_i" from 0 to 9 do { hint str _i; };
for "_i" from 10 to 0 step -1 do { ... };
for [{_i=0}, {_i<10}, {_i=_i+1}] do { ... };

// switch
switch (_color) do {
    case "red": { hint "warm"; };
    case "blue": { hint "cool"; };
    default { hint "unknown"; };
};

// forEach
{ systemChat str _x; } forEach _arr;
```

## Arrays

```sqf
_arr = [1, 2, 3];              // create
_arr = [1, 2, 3,];             // trailing comma OK

_arr select 0;                 // access (zero-based)
_arr # 0;                      // hash-select
_arr[0];                       // bracket access
_arr param [5, "default"];     // safe access with default

_arr set [0, 99];              // set element
_arr[0] = 99;                  // bracket set
_arr pushBack 4;               // append one
_arr append [5, 6];            // append multiple
_arr deleteAt 0;               // remove at index
_arr resize 10;                // resize (fills with nil)

count _arr;                    // length
_arr find 3;                   // find index (-1 if not found)
3 in _arr;                     // contains check

+_arr;                         // deep copy
_arr copy;                     // shallow copy
_arr deepCopy;                 // recursive copy

// Functional
_arr apply { _x * 2 };        // map → new array
_arr select { _x > 3 };       // filter → new array
_arr findIf { _x == 5 };      // find first match index

// Sort
_arr sort true;                // ascending
_arr sort false;               // descending
reverse _arr;                  // reverse in-place
```

## Strings

```sqf
"hello"                        // double-quoted
'hello'                        // single-quoted (same in SQ#)

"Line 1\nLine 2"               // escape sequences
@"C:\path\to\file"             // verbatim (no escapes)
f"Hello {_name}"               // interpolation

_str + _str2;                  // concatenation
count _str;                    // length
_str select 0;                 // char at index
"ello" in "Hello";             // contains
_str find "llo";               // index (-1 if not found)

format ["%1 has %2 HP", _name, _hp];  // sprintf-style
str 42;                        // "42"
parseNumber "123.45";          // 123.45
toString [65, 66];             // "AB"
toArray "AB";                  // [65, 66]
splitString ["a,b,c", ","];    // ["a","b","c"] — use binary: _str splitString ","
joinString [["a","b"], "-"];   // "a-b"
toLower _str; toUpper _str; trim _str;
```

## Math

```sqf
+ - * / % ^                    // arithmetic
abs floor ceil round sqrt      // functions
min max                        // binary compare
```

## Comparison & Logic

```sqf
== != < > <= >=                // strict comparison
&& || !                        // logical
and or not                     // word aliases
```

## Code Execution

```sqf
// call — synchronous, returns result
_result = [1, 2] call { (_this select 0) + (_this select 1) };

// spawn — asynchronous, returns ScriptHandle
_handle = spawn { sleep 5; return "done"; };
_handle = _args spawn { params ["_x"]; process(_x); };

// execVM — load file and spawn
_handle = execVM "script.sqf";

// compile — string to code at runtime
_code = compile "hint str _this;";
"hello" call _code;

// await — wait for spawned script (fiber suspends, not thread)
_result = await _handle;
_result = await _handle timeout 5;
```

## Multi-Threading

```sqf
// Spawn on named scheduler
_handle = spawnOn ["AI", { heavyWork(); }];

// With arguments
_args spawnOn ["AI", { params ["_data"]; process(_data); }];

// Thread pool (pure computation)
_handle = spawnParallel { expensiveMath(); };

// Continue with callback
_handle continueWith { systemChat str _this; };
_handle continueWithOn ["Main", { updateUI(_this); }];

// Promise combinators
PromiseAll [_h1, _h2, _h3];   // wait for all
PromiseRace [_h1, _h2];       // first wins
```

## Error Handling

```sqf
try {
    _result = _arr select _index;
} catch (_error) {
    systemChat f"Error: {_error.message}";
};
```

## Magic Variables

| Variable | Context | Value |
|---|---|---|
| `_this` | Called/spawned code | Arguments |
| `_x` | forEach/count/select/apply | Current element |
| `_y` | forEach on HashMap | Current value |
| `_forEachIndex` | forEach | Zero-based index |
| `_thisScript` | Spawned script | Own ScriptHandle |
| `_exception` | catch block | Caught exception |

## Type Checks

```sqf
isNil _var;                    // true if nil
isDefined _var;                // true if variable exists (SQ#)
typeName _val;                 // "number", "string", "array", etc.
isFrozen _arr;                 // true if immutable (SQ#)
```

## Thread Safety (SQ#)

```sqf
// Freeze — immutable snapshot (any scheduler can read)
_frozen = freeze _arr;

// Thaw — new mutable copy
_thawed = thaw _frozen;

// Channel — message passing between schedulers
_ch = Channel create;
_ch send _data;
_val = _ch receive;

// Shared — atomic counter/flag
shared _ctr = 0;
_ctr add 1;
_val = get _ctr;  // or _ctr + 0
```

## Output

```sqf
// Core (always available):
print "message";               // generic output → host.OnPrint

// Arma compat (opt-in via DeclareArmaCompatCommands):
hint "message";                // on-screen hint
systemChat "text";             // system chat
diag_log "debug";              // diagnostic log
```

## Preprocessor (Opt-In Legacy)

```sqf
// Enable with: #pragma preprocessor legacy
#define NAME value
#define MACRO(arg) code
#ifdef NAME ... #endif
#include "file.sqf"
```

## SQ# Pragmas

```sqf
#pragma sqsharp strict         // strict mode (undefined vars = error)
#pragma sqsharp compat         // SQF compatibility mode
#pragma preprocessor legacy    // enable legacy preprocessor
#pragma scheduler "AI"         // default scheduler for spawn
```

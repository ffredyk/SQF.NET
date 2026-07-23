# Type System

## SQF Data Types (Reference)

| Type | Since | Description |
|------|-------|-------------|
| Nothing | OFP | Return type of void procedures |
| Boolean | OFP | `true` / `false` |
| Number | OFP | IEEE 754 float (no int/float distinction) |
| String | OFP | `"double"` or `'single'` quoted |
| Array | OFP | Dynamic, mutable, by-reference |
| Code | OFP:R 1.85 | `{ ... }` — first-class code block |
| Object | OFP | Game entity (host-defined) |
| Group | OFP | Group reference (host-defined) |
| Side | OFP | Side/enum (host-defined) |
| Config | Arma 1 | Config path reference |
| Control | Arma 1 | UI control |
| Display | Arma 1 | UI display |
| Script Handle | Arma 1 | Spawned script reference |
| Structured Text | Arma 1 | Rich XML text |
| Location | Arma 1 | Location reference |
| Namespace | Arma 2 | Key-value namespace |
| HashMap | Arma 3 2.02 | Hash map |
| NaN | Arma 3 | Not-a-Number sentinel |

## SQ# Type System

### Core Types (Built-in)
```
Nothing (nil), Boolean, Number (double), String, Array, Code,
HashMap, Namespace, ScriptHandle, Error
```

### Host-Registered Types (Opaque References)
```
Object, Group, Side, Config, Control, Display, Location,
StructuredText, + custom host types
```

### SqValue — Tagged Union
```csharp
public readonly struct SqValue
{
    public SqType Type { get; }
    public bool AsBool();
    public double AsNumber();
    public string AsString();
    public SqArray AsArray();
    public SqCode AsCode();
    // ...
}
```

---

## Magic Types (Abstract)

SQF defines "magic types" — type-system abstractions, not real runtime types.

| Magic Type | Reality | Description |
|---|---|---|
| **Anything** | Union type | Any real type OR Nothing. nil may still fail at runtime. |
| **Nothing** | Type of nil | "No value." Return type of void procedures. |
| **Void** | Undefined variable | Variable that doesn't exist (was assigned nil or never declared). |
| **HashMapKey** | Virtual compound | Valid HashMap key types: Number, Boolean, String, Code, Side, Config, Namespace, NaN, Array. |

## nil / Nothing / Void — Critical Distinction

```
nil      — nular operator, returns Nothing value
Nothing  — type of nil. "No value."
Void     — state of undefined variable. NOT same as nil.

// SQF (original): nil DELETES variables
_myVar = nil;       // _myVar becomes Void — DELETED
isNil "_myVar";     // → true
hint str nil;       // ERROR — nil kills expressions

// SQ# (SQF-compatible): nil DELETES variables
_myVar = nil;       // _myVar becomes Void — DELETED (matches SQF)
isNil "_myVar";     // → true
isNil _myVar;       // → true (compile-time variable check)
// nil is still a valid VALUE — works in arrays, comparisons, returns:
private _arr = [1, nil, 3];
if (_arr select 1 == nil) then { ... }; // true
```

## HashMapKey — SQ# Mapping

| SQF key type | SQ# equivalent |
|---|---|
| Number, Boolean, String | Value types (naturally hashable) |
| NaN | Hashable sentinel |
| Code, Config, Namespace, Side | Identity hashing (reference equality) |
| Array | **Must be frozen** to be a key |

```csharp
map.Set(42, "answer");           // OK
map.Set(mutableArray, 2);        // THROWS
map.Set(someArray.Freeze(), 1);  // OK
```

---

## Magic Variables

Runtime-provided variables scoped to specific execution contexts.

### Core (All SQ# Scripts)

| Variable | Context | Value |
|---|---|---|
| `_this` | Called/spawned code, event handlers | Arguments |
| `_x` | forEach/count/select/apply/findIf | Current element |
| `_y` | forEach on HashMap | Current value (key = `_x`) |
| `_forEachIndex` | forEach loops | Zero-based index |
| `_thisScript` | Spawned/execVM'd script | Own ScriptHandle |
| `_exception` | catch block | Caught exception |

### Host-Defined

| Variable | Context | Value |
|---|---|---|
| `this` | Object init, triggers, waypoints | Context-dependent |
| `_thisEvent` | Event handlers | Event name |
| `_thisEventHandler` | Event handlers | Handler index |
| `_thisFSM` | FSM scripts | FSM handle |
| `_self` | HashMapObject methods | Instance reference |

### SQ# Additions

| Variable | Context | Value |
|---|---|---|
| `_error` | catch block | Structured error object |
| `_scheduler` | Any fiber | Current scheduler name |
| `_fiberId` | Any fiber | Unique fiber identifier |

---

## String Semantics

### SQF Properties

| Property | Value |
|---|---|
| Max length | ~10M chars (Arma 3 1.56+). Was 2,056 in OFP. |
| Encoding | UTF-8 |
| Quotes | `"double"` and `'single'` (since Arma 1) |
| Escapes | **None** in SQF. Use `toString [10]` for newline. |
| Preprocessor | Single-quoted strings ARE preprocessed. Double-quoted NOT. |
| Only operator | `+` |

### Key String Commands

| Command | Description |
|---|---|
| `format ["%1", val]` | sprintf-style |
| `str val` | Any → String |
| `parseNumber` | String → Number |
| `toString` / `toArray` | Char codes ↔ String |
| `count` | String length |
| `select [n]` | Char at index |
| `in` / `find` | Substring check/search |
| `splitString` / `joinString` | Split/join |
| `toLower` / `toUpper` | Case |
| `trim` | Whitespace trim |

### SQ# String Enhancements

- Escape sequences: `\n`, `\t`, `\\`, `\"`, `\'`, `\uXXXX`
- Interpolation: `f"Hello {_name}, HP: {_hp}/{_maxHp}"`
- Verbatim: `@"C:\path\to\file"`
- Multi-line: `""" ... """`
- Regex: `_str =~ /pattern/`

---

## Namespace Semantics

Namespaces are key-value containers for global variables.

### SQF Namespaces

| Namespace | Since | Lifetime | Serialized? |
|---|---|---|---|
| `missionNamespace` | Arma 2 | Mission | ✅ (saveGame) |
| `parsingNamespace` | Arma 2 | Session | ❌ |
| `uiNamespace` | Arma 2 | Session | ❌ |
| `profileNamespace` | TKOH | Profile | ✅ (profile) |
| `localNamespace` | Arma 3 2.00 | Mission | ❌ |
| `serverNamespace` | Arma 3 2.06 | Session | ❌ |
| `missionProfileNamespace` | Arma 3 2.10 | Profile | ✅ |

### Operations
```sqf
missionNamespace setVariable ["myVar", 42];
_value = missionNamespace getVariable ["myVar", 0];
with uiNamespace do { myCtrlVar = 5; };
_vars = allVariables missionNamespace;
_ns = currentNamespace;
```

### SQ# Namespace Model

- **`global` keyword**: `global SERVER_FPS = 60;` — explicit global.
- **Namespaces**: Type still exists. Host registers `missionNamespace`, etc.
- **`getVariable`/`setVariable`**: Same API. `with`/`do` supported.
- **Scheduler-local**: Each scheduler has own copy of globals (thread safety).

---

## params / param Validation

```sqf
// Basic
params ["_one", "_two", "_three"];

// With defaults + validation
params [
    "_name",                              // required
    ["_age", 0, [0]],                    // default 0, must be Number
    ["_tags", [], [[]], [2, 4]],         // default [], Array of 2-4
    ["_optional", nil]
];

// Returns false if default used
if (!params ["_req", ["_opt", 42]]) exitWith { hint "Defaults!"; };

// Skip: params ["", "", "_third"];
// Auto-wrap: 123 call { params ["_x"]; };  // _x = 123
```

### SQ# params Enhancements
```sqf
params [_name: string, _age: int = 0, _tags: string[] = []];
params [_x, _y, _z]: Vector3;          // destructuring
params [_first, ..._rest];             // rest params
```

---

## isEqualTo vs ==

| Behavior | SQF `==` | SQF `isEqualTo` | SQ# `==` |
|---|---|---|---|
| Type coercion | Yes (quirky) | No | No (strict) |
| String comparison | Case-insens. | Case-sensitive | Case-sensitive |
| Different types | May error | false | false |
| Arrays | ❌ error | ✅ deep | ✅ deep |
| nil comparison | Error-prone | Nothing (bug!) | true/false |
| Namespaces, Handles | ❌ | ✅ | ✅ |
| Booleans | Error before 2.00 | Fine | Fine |

SQ# makes `==` strict (`isEqualTo` semantics). `isEqualTo` kept as alias. `nil == nil` → `true`. `nil == x` → `false`.

---

## Preprocessor (Legacy Opt-In)

SQ# provides opt-in legacy preprocessor for SQF compatibility.

| Directive | Support |
|---|---|
| `#define`, `#define(args)`, variadic (`...`, `__VA_ARGS__`, etc.) | ✅ |
| `#undef` | ✅ |
| `#if`/`#ifdef`/`#ifndef`/`#else`/`#endif` (including nested) | ✅ |
| `#include "path"` | ✅ |
| `#` stringify, `##` concat | ✅ |
| `__LINE__`, `__FILE__`, `__has_include` | ✅ |
| Date/time builtins | ✅ |
| `__COUNTER__`, `__RAND_INT*__`, `__GAME_VER*__` | ✅ (host-defined) |
| `__EXEC`, `__EVAL` | ❌ (Config Parser only) |

SQ# `#pragma` directives (always available):
- `#pragma sqsharp strict` / `compat`
- `#pragma preprocessor legacy`
- `#pragma scheduler "name"`

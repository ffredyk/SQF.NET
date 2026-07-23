# SQ# (SQF Sharp) — Implementation Plan

## Decisions
- **Scope**: Core language + extensible host API (no Arma commands built-in)
- **Compatibility**: Modernized SQF dialect (not drop-in compatible)
- **Execution**: Stack-based bytecode VM
- **Scheduling**: Hybrid — cooperative per scheduler, multi-scheduler on different threads
- **Integration**: Standalone CLI + NuGet library
- **.NET**: .NET 10
- **Name**: SQ# (SQF Sharp)
- **Preprocessor**: Hybrid — module system (default) + optional preprocessor pass for legacy .sqf
- **Serialization**: Binary .sqfc bytecode format
- **First milestone**: Full language + host demo
- **Unity**: Design for it, build later

## Core SQF Syntax (aligned with Bohemia Interactive wiki)

Sources:
- https://community.bistudio.com/wiki/SQF_Syntax
- https://community.bistudio.com/wiki/Order_of_Precedence
- https://community.bistudio.com/wiki/Operators
- https://community.bistudio.com/wiki/Control_Structures
- https://community.bistudio.com/wiki/Data_Type
- https://community.bistudio.com/wiki/Anything
- https://community.bistudio.com/wiki/Nothing
- https://community.bistudio.com/wiki/Void
- https://community.bistudio.com/wiki/HashMapKey
- https://community.bistudio.com/wiki/For_Type
- https://community.bistudio.com/wiki/If_Type
- https://community.bistudio.com/wiki/While_Type
- https://community.bistudio.com/wiki/Switch_Type
- https://community.bistudio.com/wiki/Array
- https://community.bistudio.com/wiki/Script_Handle
- https://community.bistudio.com/wiki/Code
- https://community.bistudio.com/wiki/call
- https://community.bistudio.com/wiki/spawn
- https://community.bistudio.com/wiki/execVM
- https://community.bistudio.com/wiki/compile
- https://community.bistudio.com/wiki/Scheduler
- https://community.bistudio.com/wiki/String
- https://community.bistudio.com/wiki/Namespace
- https://community.bistudio.com/wiki/params
- https://community.bistudio.com/wiki/Magic_Variables
- https://community.bistudio.com/wiki/private
- https://community.bistudio.com/wiki/isEqualTo
- https://community.bistudio.com/wiki/PreProcessor_Commands

### Fundamental Design: Operator-Based Language
SQF has **barely any language structures**. Everything is provided via operators (scripting commands).
**Including control flow.** `if`, `while`, `for`, `switch` are just operators — NOT special syntax.
They chain via **Helper Types** (If Type, While Type, For Type, Switch Type, With Type).

Three operator arities form the backbone:

| Type | Signature | Example | Behavior |
|------|-----------|---------|----------|
| **Nular** (nullar) | `operator` — no args | `allUnits`, `player`, `nil` | Returns computed state each call. NOT a cached variable — re-evaluates every access. |
| **Unary** | `operator <right>` | `count _arr`, `str 123`, `isNil "_x"` | Greedy: consumes immediate right-side value. `count _arr select 2` means `(count _arr) select 2`. |
| **Binary** | `<left> operator <right>` | `_a + _b`, `_arr select 0` | Resolved by precedence; equal precedence → left-to-right. |

### Statement Termination
- `;` (preferred, conventional) or `,` (also valid)
- Not line-based — multiple expressions on one line OK

### Brackets
- `()` — Override precedence or group expressions
- `[]` — Array literals
- `{}` — Code block values. Also used in control structures

### Whitespace & Blank Lines
- Tabs/spaces ignored. "Line" begins at first non-whitespace.
- Blank lines (whitespace-only) ignored.

### Comments (Preprocessor Phase)
- `//` — line comment
- `/* */` — block comment (can span lines, can appear mid-expression)
- Removed during **preprocessing** — won't survive in strings passed to `compile` or `loadFile`
- Legacy `comment` operator exists (backward compat, actually executes — DON'T use)

### Assignment
- Only `=` operator. No `+=`, `-=`, `*=`, `/=`.
- Assignment IS an expression — returns assigned value.
- Arrays assigned **by reference**. Use unary `+` to copy: `_arrB = +_arrA;`

---

## Full Operator Precedence Table

Higher number = higher priority. Equal precedence → left-to-right associativity.

| Precedence | Category | Operators |
|:---:|---|---|
| **11** | Nular operators, values, brackets | variables, literals, `()`, `[]`, `{}`, `""`, `''` |
| **10** | Unary operators | `+a`, `-a`, `!a`, `not a`, `count _arr`, `str _val`, etc. |
| **9** | Hash-select | `array # index` |
| **8** | Power | `a ^ b` |
| **7** | Multiply/Divide/Remainder, atan2, Config `/` | `*`, `/`, `%`, `mod`, `atan2` |
| **6** | Add/Subtract (number, array, string), min, max | `+`, `-`, `min`, `max` |
| **5** | else (if-then-else chaining) | `else` |
| **4** | Binary commands (general) | `setDir`, `switch` colon `:`, `select`, `set`, `resize`, etc. |
| **3** | Comparisons, Config `>>` | `==`, `!=`, `>`, `<`, `>=`, `<=`, `>>` |
| **2** | Logical AND | `&&`, `and` |
| **1** | Logical OR | `\|\|`, `or` |

### Key Precedence Insights

1. **Unary (10) > Binary (4)**: `count _arr select 2` = `(count _arr) select 2` — NOT `count (_arr select 2)`.
2. **Arithmetic (7,6) > Binary commands (4)**: `a + b select 0` = `(a + b) select 0`.
3. **Comparisons (3) > Logic (2,1)**: `a == b && c == d` = `(a == b) && (c == d)`.
4. **else at 5**: sits BETWEEN add/sub (6) and binary commands (4). Dictates how `if-then-else` chains.
5. **`*`/`/` (7) before `+`/`-` (6)**: standard math.
6. **`select` at 4**: lower than arithmetic, higher than comparisons. `a select 0 == b` = `(a select 0) == b`.

---

## Control Structures = Operators + Helper Types

**Critical insight from wiki**: Control structures are NOT special syntax. They're normal operators that use
**helper types** to chain together.

### How `if`-`then`-`else` Works

```
if (CONDITION) then { CODE1 } else { CODE2 };
```

Internal operator chain:
1. `if` — **unary** operator (prec 10). Takes condition → returns **If Type**.
2. `then` — **binary** operator. Takes `If Type` + `Code` → evaluates condition: true→runs code1 and returns its result; false→returns Nothing (but with hook for else).
3. `else` — **binary** operator (prec 5). Takes left-value (result of then) + right `Code`. Packs both into `[Code, Code]` array, feeds to `then`.

Equivalent: `if (cond) then [{code1}, {code2}];` — else just sugar for array-packing.

**Parser handling**:
```csharp
// SQ# will parse if-then-else as syntactic sugar while exposing raw operators:
// Sugar form (preferred):
if (condition) { code1 } else { code2 }
// Raw operator form (available for host extensibility):
if (condition) then { code1 } else { code2 }
```

### How `while`-`do` Works

```
while { CONDITION } do { BODY };
```

1. `while` — **unary** operator. Takes condition Code → returns **While Type**.
2. `do` — **binary** operator. Takes `While Type` + `Body Code` → executes loop, returns last body result.

### How `for` Works (Two Forms)

**Array form:**
```
for [{INIT}, {CONDITION}, {STEP}] do { BODY };
```
1. `for` — takes Array of 3 Code blocks → returns **For Type**.
2. `do` — binary, takes `For Type` + `Body Code`.

**From-to-step form:**
```
for "VARNAME" from START to END step STEP do { BODY };
```
Each word (`for`, `from`, `to`, `step`) returns/consumes **For Type**:
1. `for "VARNAME"` → For Type
2. `from START` → binary on For Type + Number
3. `to END` → binary on For Type + Number
4. `step STEP` → binary on For Type + Number (optional, defaults to 1)
5. `do { BODY }` → binary on For Type + Code

### How `switch`-`do`-`case`-`default` Works

```
switch (VARIABLE) do {
    case VALUE1: { CODE1 };
    case VALUE2: { CODE2 };
    default { CODE3 };
};
```

1. `switch` — **unary**. Takes variable → returns **Switch Type**.
2. `do` — binary. Takes `Switch Type` + `Code` (the cases block).
3. Inside the block: `case VALUE:` — colon `:` at prec 4 acts as binary: takes VALUE + Code → registers case.
4. `default` — nular inside switch context.
5. Block returns Switch Type; when fully evaluated, switch resolves to matching case's result.

### SQ# Approach: Sugar by Default, Raw Operators Available

For SQ# we provide **clean syntax** for users BUT expose the underlying operator chaining via the Host API
so hosts can create their own control-flow-like DSL constructs.

```csharp
// Host can register operators that return helper types:
host.RegisterUnary("repeat", arg => new RepeatType(arg));
host.RegisterBinary("times", (left, right) => {
    if (left is RepeatType rt) {
        for (int i = 0; i < rt.Count; i++) call right;
        return SqValue.Nil;
    }
    throw new SqTypeError("times expects RepeatType on left");
}, precedence: 4);
```

---

## Data Types

### Core Value Types (SQF)

| Type | Since | Description |
|------|-------|-------------|
| **Nothing** (void) | OFP | Return type of procedures, unset variables |
| **Boolean** | OFP | `true` / `false` |
| **Number** | OFP | IEEE 754 float (scalar, no int/float distinction) |
| **String** | OFP | `"double"` or `'single'` quoted |
| **Array** | OFP | Dynamic, mutable, by-reference |
| **Code** | OFP:R 1.85 | `{ ... }` — first-class function/code block value |
| **Object** | OFP | Game entity reference (host-defined) |
| **Group** | OFP | Group reference (host-defined) |
| **Side** | OFP | Side/enum reference (host-defined) |
| **Config** | Arma 1 | Config path reference |
| **Control** | Arma 1 | UI control reference |
| **Display** | Arma 1 | UI display reference |
| **Script Handle** | Arma 1 | Handle to spawned script/fiber |
| **Structured Text** | Arma 1 | Rich text with XML markup |
| **Location** | Arma 1 | Location reference |
| **Namespace** | Arma 2 | Key-value namespace (like HashMap) |
| **HashMap** | Arma 3 2.02 | Hash map type |
| **NaN** | Arma 3 | Not-a-Number sentinel |

### SQ# Type System

Core value types in SQ#:
```
Nothing (nil), Boolean, Number (double), String, Array, Code, 
HashMap, Namespace, ScriptHandle, Error
```

Host-registered types (opaque references):
```
Object, Group, Side, Config, Control, Display, Location, 
StructuredText, + custom host types
```

All values boxed as `SqValue` (struct wrapping tagged union):
```csharp
public readonly struct SqValue
{
    public SqType Type { get; }
    // Internal storage: union of double, bool, string ref, object ref, array ref, code ref...
    public bool AsBool();
    public double AsNumber();
    public string AsString();
    public SqArray AsArray();
    public SqCode AsCode();
    // ...
}
```

---

### Magic Types (Abstract / Non-Concrete)

SQF defines several "magic types" that are NOT real runtime types — they're type-system abstractions
for documentation and semantic purposes.

| Magic Type | Reality | Description |
|---|---|---|
| **Anything** | Union type | Accepts any real type OR Nothing. Used in command signatures. Warning: even though a command says it takes Anything, passing `nil` may still fail at runtime. |
| **Nothing** | The type of `nil` | "No value." Return type of void procedures like `hint`. Every expression must return SOME type — Nothing fills this gap. Cannot meaningfully be assigned. Checked via `isNil`. |
| **Void** | Undefined variable | A variable that doesn't exist or was undefine'd. Distinct from `nil` — assigning `nil` to a variable **deletes** it (makes it Void), it doesn't set it to Nothing. |
| **HashMapKey** | Virtual compound | Not a real type. Represents the set of types usable as HashMap keys: Number, Boolean, String, Code, Side, Config, Namespace, NaN, Array. |

#### The nil / Nothing / Void Relationship (Critical Nuance)

```
nil          — nular operator, returns the Nothing value
Nothing      — type of nil. "No value." Return type of void expressions.
Void         — state of an undefined variable. NOT the same as nil.

// SQF behavior:
_myVar = 42;        // _myVar is Number = 42
_myVar = nil;       // _myVar is now Void (undefined!) — NOT "nil value"
isNil "_myVar";     // → true after assigning nil

// nil itself IS a value — you just can't store it in a variable:
_val = nil;         // _val becomes Void (the variable is DELETED)
hint str nil;       // ERROR: str says it takes Anything, but nil kills it
```

Key insight: **`nil` is not a "null value"** — it's an **unassignment operator**. Assigning nil deletes the variable.
This is different from most languages where `null` IS a storable value.

#### Void Type Inference (SQF Quirk)

SQF infers types even for undefined variables:
```sqf
a = nil;          // a is Void (undefined)
b = a + [];       // b is Void BUT with inferred type Array
// str b → "array" (the TYPE name string, not the value)
```

Comparison with undefined variables **always errors**:
```sqf
if (undefinedVar == definedVar) then { ... };   // ERROR
if (undefinedVar == undefinedVar) then { ... }; // ERROR
```

Detect via `isNil "varName"` (string argument — looks up by name).

#### SQ# Approach: SQF-Compatible nil/Void

SQ# preserves SQF's nil semantics — nil assignment deletes variables. This is the expected behavior for SQF scripters:

| Concept | SQ# Approach |
|---|---|
| `nil` literal | Value of type `Nothing`. `_x = nil;` → `_x` DELETED (matches SQF). |
| `Nothing` type | Proper unit type. One value: `nil`. Cannot be stored in variables. |
| Undefined variables | **Error** at runtime `UndefinedVariableError` when accessed directly. |
| Unset a variable | Assign `nil` to it. `_x = nil;` deletes `_x`. |
| `isNil` | Compile-time for variables: `isNil _x` → true if var undefined. String form: `isNil "varName"` checks by name. Value form: `isNil expr` checks if value is nil. |

```sqf
// SQ# SQF-compatible behavior:
_myVar = 42;       // Number = 42
_myVar = nil;      // Variable DELETED (SQF semantics)
isNil _myVar;      // → true (variable gone — compile-time check)
isNil "_myVar";    // → true (string form — looks up global)

// nil is still a value — works in arrays and comparisons:
private _arr = [1, nil, 3];
if (_arr select 1 == nil) then { ... }; // true
_undefined;        // ERROR: Undefined variable '_undefined'
```

This makes SQ# behave like a normal modern language while keeping `nil` familiar to SQF users.

### HashMapKey — SQ# Mapping

In SQ#, HashMap keys can be any **value-typed** or **immutable** type. This maps naturally:

| SQF HashMapKey types | SQ# equivalent |
|---|---|
| Number, Boolean, String | Value types (naturally hashable) |
| NaN | Supported as hashable sentinel |
| Code, Config, Namespace, Side | Reference types with **identity** hashing (reference equality) |
| Array | **Must be frozen/immutable** to be a key. Mutable arrays error at insert time. |

```csharp
// SQ# HashMap implementation:
var map = new SqHashMap();
map.Set(42, "answer");           // Number key — OK
map.Set(true, "yes");            // Boolean key — OK
map.Set("name", "value");        // String key — OK
map.Set(someArray.Freeze(), 1);  // Immutable array — OK
map.Set(mutableArray, 2);        // THROWS: mutable arrays can't be HashMap keys
```

---

## Operator Semantics (Detailed)

### Arithmetic
| Op | Arity | Signature | Notes |
|----|-------|-----------|-------|
| `+` | Unary (prec 10) | `Number → Number` | Identity (duplication) |
| `-` | Unary (prec 10) | `Number → Number` | Negation |
| `+` | Binary (prec 6) | `Number×Number → Number` | Addition |
| `-` | Binary (prec 6) | `Number×Number → Number` | Subtraction |
| `*` | Binary (prec 7) | `Number×Number → Number` | Multiplication |
| `/` | Binary (prec 7) | `Number×Number → Number` | Division |
| `%` | Binary (prec 7) | `Number×Number → Number` | Modulo |
| `mod` | Binary (prec 7) | `Number×Number → Number` | Modulo (alias) |
| `^` | Binary (prec 8) | `Number×Number → Number` | Power |

### Array
| Op | Arity | Signature | Notes |
|----|-------|-----------|-------|
| `+` | Unary (prec 10) | `Array → Array` | **Deep copy** — recursively copies sub-arrays (SQF quirk: deep, not shallow) |
| `+` | Binary (prec 6) | `Array×Array → Array` | **Concatenation** — `[1,2] + [3,2]` = `[1,2,3,2]` |
| `-` | Binary (prec 6) | `Array×Array → Array` | **Removal** — `[1,2,3,2,4] - [2,3]` = `[1,4]` |

### String
| Op | Arity | Signature | Notes |
|----|-------|-----------|-------|
| `+` | Binary (prec 6) | `String×String → String` | **Concatenation** — only string operator |

### Logical
| Op | Arity | Signature | Notes |
|----|-------|-----------|-------|
| `!` | Unary (prec 10) | `Boolean → Boolean` | NOT |
| `not` | Unary (prec 10) | `Boolean → Boolean` | NOT (alias) |
| `&&` | Binary (prec 2) | `Boolean×Boolean → Boolean` | AND |
| `and` | Binary (prec 2) | `Boolean×Boolean → Boolean` | AND (alias) |
| `\|\|` | Binary (prec 1) | `Boolean×Boolean → Boolean` | OR |
| `or` | Binary (prec 1) | `Boolean×Boolean → Boolean` | OR (alias) |

No built-in XOR, NOR, NAND — emulate via combinations.

### Comparison
| Op | Arity | Signature | Notes |
|----|-------|-----------|-------|
| `==` | Binary (prec 3) | `Any×Any → Boolean` | Equality |
| `!=` | Binary (prec 3) | `Any×Any → Boolean` | Inequality |
| `<` | Binary (prec 3) | `Number×Number → Boolean` | Less than |
| `>` | Binary (prec 3) | `Number×Number → Boolean` | Greater than |
| `<=` | Binary (prec 3) | `Number×Number → Boolean` | Less or equal |
| `>=` | Binary (prec 3) | `Number×Number → Boolean` | Greater or equal |

---

## Array Semantics (Detailed)

Source: https://community.bistudio.com/wiki/Array

### Core Properties

| Property | SQF Behavior |
|---|---|
| **Types** | Heterogeneous — any type can coexist in same array |
| **Size limit** | 9,999,999 (sometimes 10,000,000) elements since Arma 3 v1.56 |
| **Storage** | Reference type. `_b = _a` shares same array. |
| **Indexing** | Zero-based. `_arr select 0` or `_arr # 0` (Arma 3 hash-select). |
| **Trailing comma** | NOT allowed. `[1,2,]` → "Unexpected ," error. |
| **Read-only arrays** | Some (trigger thisList, addon configs) are frozen. Must copy with `+` before mutating. |

### Index Behavior (SQF Quirks)

This is one of the most unusual parts of SQF. For array `["element0"]` (count = 1):

```sqf
_arr select -1;  // Error Zero Divisor       — negative = always error
_arr select  0;  // "element0"               — valid range
_arr select  1;  // nil                      — index == count: soft out-of-bounds, returns nil
_arr select  2;  // Error Zero Divisor       — index > count: hard error
```

**Rule**: Only exactly ONE past the end returns nil. Any further → error. Any negative → error.
This is inconsistent and a common source of bugs.

**Index rounding**: Indices are floats rounded to nearest integer using **banker's rounding** (X.5 → nearest even).
```
-0.5 → 0 (up)
 0.5 → 0 (down to even)
 1.5 → 2 (up to even)
 2.5 → 2 (down to even)
```

**`param`** — safe alternative to `select`. Returns nil or specified default on out-of-range:
```sqf
_arr param [5];         // nil (instead of error)
_arr param [5, "abc"];  // "abc" (default value)
```

### set — Index Assignment with Auto-Resize

| Index | Behavior |
|---|---|
| In range | Replaces element. |
| Positive, out of range | Array **resizes** to include index as last element. Gaps filled with `nil`. |
| Negative | Error Zero Divisor. |

```sqf
_arr = ["a"];
_arr set [3, "d"];  // _arr is now ["a", nil, nil, "d"] — auto-resized!
```

### resize — Explicit Size Change

```sqf
_arr = ["a", "b", "c", "d", "e"];
_arr resize 3;  // ["a", "b", "c"] — truncate

_arr = ["a", "b", "c"];
_arr resize 5;  // ["a", "b", "c", nil, nil] — expand with nil fill
```

### Mutation vs Copy Operations

| Operation | Mutates? | Returns | Notes |
|---|---|---|---|
| `_arr set [i, v]` | ✅ | Nothing | Auto-resizes on positive OOB |
| `_arr pushBack v` | ✅ | Index (int) | Adds to end |
| `_arr append [a,b]` | ✅ | Nothing | Adds multiple to end |
| `_arr deleteAt i` | ✅ | Deleted element | Removes at index |
| `_arr deleteRange [i, n]` | ✅ | Nothing | Removes n elements from i |
| `_arr resize n` | ✅ | Nothing | Shrink or expand |
| `_arr sort asc` | ✅ | Nothing | In-place sort |
| `reverse _arr` | ✅ | Nothing | In-place reverse |
| `_arr + [x]` | ❌ (copy) | New array | Concatenation |
| `_arr - [x]` | ❌ (copy) | New array | Removal of all matching |
| `+_arr` | ❌ (copy) | New array | Shallow copy of array, **deep copy of sub-arrays** |
| `_arr apply { ... }` | ❌ (copy) | New array | Like JS map |
| `_arr select { cond }` | ❌ (copy) | New array | Filtered new array |
| `_arr arrayIntersect _arr2` | ❌ (copy) | New array | Set intersection |

**Critical**: Unary `+` on arrays does a **deep copy of sub-arrays**, not just shallow:
```sqf
_a = [[1,2], [3,4]];
_b = +_a;
(_a select 0) set [0, 99];  // _a[0] changes, _b[0] NOT affected — deep copy!
```

### Array Subtraction Semantics

Subtracts **ALL** occurrences of right-side elements from left:
```sqf
["a", "b", "c", "a", "b", "c"] - ["a", "b"];  // ["c", "c"] — all a's and b's gone
```

To remove single instance: use `deleteAt` with `find`, or the `set`+objNull trick.

### Functional Array Operations

```sqf
// apply — map (returns new array)
[1,2,3] apply { _x * 2 };           // [2,4,6]

// select with code — filter (returns new array)
[1,2,3,4,5] select { _x > 3 };      // [4,5]

// findIf — find first match index (stops early)
[1,2,3,4,5] findIf { _x == 3 };     // 2

// arrayIntersect — set intersection (also removes duplicates!)
[1,2,2,3,4] arrayIntersect [1,2,2,3,4];  // [1,2,3,4] — unique!
```

### Sorting

```sqf
_arr sort true;    // ascending  ["aaa","bbb","ccc"]
_arr sort false;   // descending [1024, 666, 57, 42]
// Works on strings, numbers, and sub-arrays (sorts by first element)
[["zzz",0], ["aaa",42], ["ccc",33]] sort true;  // [["aaa",42], ["ccc",33], ["zzz",0]]

reverse _arr;      // reverse in-place
```

---

### SQ# Array Design — Cleaned Up

SQF array behavior has many footguns. SQ# modernizes while keeping the spirit.

| Issue | SQF | SQ# |
|---|---|---|
| **select OOB** | -1→error, count→nil, count+1→error | `_arr[idx]` → `nil` for any OOB index. `_arr select idx` same. Strict mode: compile error if index provably OOB. |
| **Index rounding** | Banker's rounding on float | If index is `Number` (float) → banker's rounding (compat). If `int` → exact. |
| **set OOB** | Auto-resize with nil fill | `_arr set [i, v]` → auto-resize (kept, useful). `_arr[i] = v` → same. |
| **nil in arrays** | nil fills gaps, but nil == deleted variable | `nil` is a storable value. `[1, nil, 3]` is valid. |
| **Copy depth** | `+_arr` = deep copy of sub-arrays (sometimes unexpected) | `_arr copy` = shallow copy. `_arr deepCopy` = recursive copy. `+_arr` kept for compat. |
| **Subtract all** | `_arr - [x]` removes ALL x | Same. `_arr removeFirst x` for single removal. |
| **Trailing comma** | Error | Allowed: `[1, 2, 3,]` is fine (quality of life). |
| **Read-only arrays** | Error on mutation | `FrozenArray` type. Explicit `.freeze()` / `.thaw()` (returns new mutable copy). |
| **Size limit** | ~10M elements | No hard limit (memory-bound). Configurable cap if host wants. |
| **forEach** | `{ ... } forEach _arr` | Same, plus `for (x in _arr) { ... }` syntax sugar. |
| **Hash-select** | `_arr # idx` (Arma 3 only) | `_arr[idx]` bracket syntax preferred. `_arr # idx` supported. |

```sqf
// SQ# array usage:
private _arr = [1, 2, 3, 4, 5];

// Access
_arr[0];              // 1
_arr[99];             // nil (safe OOB)
_arr select 0;        // 1 (legacy syntax)

// Mutation
_arr pushBack 6;      // [1,2,3,4,5,6]
_arr append [7, 8];   // [1,2,3,4,5,6,7,8]
_arr deleteAt 0;      // [2,3,4,5,6,7,8]
_arr[0] = 99;         // [99,3,4,5,6,7,8] — bracket assignment
_arr set [1, 100];    // [99,100,4,5,6,7,8] — legacy syntax

// Functional (return new arrays)
_arr apply { _x * 2 };          // new array
_arr select { _x > 4 };         // filtered new array
_arr findIf { _x == 5 };        // index or -1

// Copy
_arr2 = _arr copy;              // shallow copy
_arr2 = _arr deepCopy;          // recursive copy
_arr2 = +_arr;                  // deep copy (SQF compat — kept as alias for deepCopy)

// Freeze / thaw
private _frozen = _arr freeze;  // immutable FrozenArray
_frozen[0] = 5;                 // RUNTIME ERROR: frozen array
private _thawed = _frozen thaw; // new mutable copy
```

### Standard Array Commands (StdLib)

Core commands provided by `SQSharp.StdLib`:

| Category | Commands |
|---|---|
| **Access** | `select`, `#` (hash-select), `param`, `params` |
| **Mutation** | `set`, `pushBack`, `pushBackUnique`, `append`, `deleteAt`, `deleteRange`, `insert` |
| | (SQ# additions) |
| **Size** | `count`, `resize`, `isEmpty` (SQ# addition) |
| **Search** | `find`, `findIf`, `in`, `arrayIntersect` |
| **Functional** | `apply`, `select` (filter), `reduce` (SQ# addition), `all`, `any` (SQ# additions) |
| **Sort** | `sort`, `reverse`, `shuffle` (SQ# addition) |
| **Copy** | `+` (unary, deep), `copy`, `deepCopy`, `freeze`, `thaw` |
| **Set ops** | `+` (concat), `-` (remove all), `arrayUnion` (SQ# addition) |

---

## Script Handle & Promise System

Source: https://community.bistudio.com/wiki/Script_Handle

### SQF Script Handle Basics

| Feature | Description |
|---|---|
| **Created by** | `spawn`, `execVM` — returns Script Handle |
| **Status check** | `scriptDone _handle` → Boolean. `isNull _handle` (Arma 3) — completed handle becomes null. |
| **Termination** | `terminate _handle` — kills running script. |
| **Self-reference** | `_thisScript` magic variable (Arma 3 1.54+) — script's own handle. |
| **Introduced** | Arma 1 (not in OFP). |

### SQF Promise Handles (Arma 3 2.22+)

Script Handles double as **Promises**. Every spawned script is a promise that resolves when the script exits.

| Operation | Syntax | Description |
|---|---|---|
| **Create empty promise** | `_h = spawn "Name";` | No backing script — pure promise holder. Manually resolve via `terminate`. |
| **Create real promise** | `_h = 0 spawn { ... };` | Script runs, handle resolves with return value when script exits. |
| **Resolve promise** | `_h terminate _value;` | Completes promise with value. Kills backing script if running. |
| **Await result** | `_result = waitUntil _h;` | Block until promise resolves, return its value. Scheduled only. |
| **Await with timeout** | `_result = waitUntil [_h, 60];` | Block up to timeout seconds. Returns `nil` if timeout. |
| **Add continuation** | `_h continueWith { ... };` | Register callback. `_this` = resolved value. Runs when promise completes. |
| **Check status** | `scriptDone _h` / `isNull _h` | Whether promise has resolved. |

**Promise example (SQF):**
```sqf
// Create promise without backing script
_handle = spawn "MyPromise";

// Some async work completes later...
_handle terminate "result value";

// Consumer can await it:
_result = waitUntil _handle;  // "result value"

// Or use continuation (non-blocking, works in unscheduled code):
_handle continueWith { systemChat format ["Got: %1", _this]; };
```

---

### SQ# Promise System — Enhanced for Multithreading

SQ# takes SQF promises and elevates them to **proper async/await with real multithreading**.
Every Script Handle IS a `Task<SqValue>` under the hood, giving full .NET async ecosystem integration.

#### Architecture

```
┌─────────────────────────────────────────────────────┐
│                  ScriptHandle<T>                     │
│  ┌──────────────┐    ┌──────────────────────────┐   │
│  │ SQF compat   │    │ .NET Task integration     │   │
│  │ surface      │    │                          │   │
│  │ scriptDone   │    │ .ToTask() → Task<T>      │   │
│  │ terminate    │    │ .ContinueWith()           │   │
│  │ isNull       │    │ .GetAwaiter() → await    │   │
│  │ continueWith │    │ CancellationToken        │   │
│  └──────────────┘    └──────────────────────────┘   │
│                                                      │
│  Fiber binding:                                      │
│  ┌─────────────────────────────────────────────────┐ │
│  │ Scheduler A (Thread 1)   Scheduler B (Thread 2) │ │
│  │ [Fiber: waitUntil _h]    [Fiber: spawn {cpu}]   │ │
│  │      ↓                        ↓                 │ │
│  │  Fiber SUSPENDED         Fiber RUNNING          │ │
│  │  (awaiting promise)      (CPU-bound work)       │ │
│  │      ↓                        ↓                 │ │
│  │  Fiber RESUMED           Promise RESOLVED       │ │
│  │  (on Scheduler A)        (signal back to A)     │ │
│  └─────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

#### Key Enhancements Over SQF

| Feature | SQF | SQ# |
|---|---|---|
| **Threading** | All promises on same scheduler | Resolve across schedulers, threads, or even processes |
| **C# interop** | Not possible | `await handle.ToTask()` in C# host code |
| **Cancellation** | `terminate` only | `CancellationToken` — propagate cancellation through chain |
| **Error handling** | Silent fail, manual check | Exceptions captured, propagated through promise chain |
| **Continuation scheduling** | Same scheduler | Specify: same scheduler, specific scheduler, thread pool |
| **Promise combinators** | None | `Promise.all([h1, h2])`, `Promise.race([...])`, `Promise.any([...])` |
| **Timeout** | `waitUntil [h, t]` | `.Timeout(seconds)` → `TimeoutException` on expiry |
| **Empty promise** | `spawn "Name"` | `Promise.create("Name")` — explicit, clearer |
| **Await in unscheduled** | ❌ (must use continueWith) | ✅ `await _handle` in any fiber (suspends fiber, not thread) |
| **Progress reporting** | ❌ | `IProgress<T>` support for long-running scripts |

#### Promise Combinators (SQ# Additions)

```sqf
// All — wait for all promises, get array of results
private _results = PromiseAll [_h1, _h2, _h3];
// _results = [result1, result2, result3]

// Race — first promise to resolve wins, others cancelled
private _winner = PromiseRace [_h1, _h2];
// Returns result of whichever finishes first

// Any — first promise to resolve successfully (ignore errors unless all fail)
private _first = PromiseAny [_h1, _h2, _h3];
```

#### C# Host Integration

```csharp
// C# host can create, await, and resolve SQ# promises directly:
var handle = vm.Spawn(codeBlock, scheduler: SchedulerA);

// Await in C# async method:
SqValue result = await handle.ToTask();

// Or continue with callback:
handle.ContinueWith(val => Console.WriteLine($"Script returned: {val}"));

// Cancellation:
var cts = new CancellationTokenSource();
var handle = vm.Spawn(codeBlock, cancellationToken: cts.Token);
cts.Cancel();  // equivalent to terminate

// LINQ-style chaining:
var result = await vm.Spawn(codeBlock)
    .Timeout(TimeSpan.FromSeconds(5))
    .ContinueWith(val => val.AsNumber() * 2)
    .ToTask();
```

#### SQ# Script Syntax

```sqf
// Spawn on specific scheduler
private _handle = spawnOn "AI" { heavyComputation() };

// Spawn on thread pool for true parallel CPU work
private _handle = spawnParallel { expensiveMath() };

// Await (fiber suspends, not thread — cooperative)
private _result = await _handle;          // blocks fiber, yields to scheduler
private _result = await _handle timeout 5; // with timeout

// Continue with (non-blocking)
_handle continueWith { systemChat str _this; };

// Continue on specific scheduler
_handle continueWithOn "Main" { updateUI(_this); };

// Promise combinators
private _allDone = PromiseAll [
    spawn { loadAssets1() },
    spawn { loadAssets2() },
    spawn { loadAssets3() }
];

// Error handling
try {
    private _result = await _handle;
} catch (_error: ScriptTimeoutError) {
    systemChat "Script timed out!";
} catch (_error: ScriptTerminatedError) {
    systemChat "Script was terminated!";
}

// Progress
private _handle = spawn {
    for "_i" from 0 to 100 do {
        progress _i;          // report progress 0-100
        sleep 0.01;
    };
    return "done";
};
_handle onProgress { hint format ["Loading: %1%%", _this]; };
```

#### Scheduler-Aware Spawning

```sqf
// Default: spawn on current scheduler
_h1 = spawn { ... };

// Named scheduler (registered by host)
_h2 = spawnOn "AI" { ... };         // AI processing scheduler
_h3 = spawnOn "Main" { ... };       // Main/game thread scheduler
_h4 = spawnOn "IO" { ... };         // I/O scheduler

// Thread pool (true parallelism for CPU work)
_h5 = spawnParallel { heavyMath() };
// ⚠️ spawnParallel code runs on .NET thread pool.
// Host commands may not be available depending on host thread-safety.
// Use for pure computation only.

// Remote (🏠 host: cross-process networking)
// _h6 = spawnRemote "worker-node" { ... };
```

#### ScriptHandle API (StdLib)

| Command | Signature | Description |
|---|---|---|
| `scriptDone` | `Handle → Boolean` | Has promise resolved? |
| `isNull` | `Handle → Boolean` | Null/resolved handle? (Arma 3 compat) |
| `terminate` | `Handle, [Value] → Nothing` | Resolve promise + kill script. Optional result value. |
| `continueWith` | `Handle, Code → Handle` | Add continuation. Returns same handle (chainable). |
| `continueWithOn` | `Handle, Scheduler, Code → Handle` | Continuation on specific scheduler (SQ# addition). |
| `waitUntil` | `Handle → Value` | Block fiber until resolved. |
| `waitUntil` | `[Handle, Number] → Value` | Block with timeout. nil if timeout (Arma 3 compat). |
| `await` | `Handle → Value` | SQ# keyword — same as `waitUntil` but cleaner. |
| `PromiseAll` | `[Handle...] → [Value...]` | Wait for all (SQ# addition). |
| `PromiseRace` | `[Handle...] → Value` | First to resolve wins, rest cancelled (SQ# addition). |
| `PromiseAny` | `[Handle...] → Value` | First successful resolution (SQ# addition). |
| `spawnOn` | `Scheduler, Code → Handle` | Spawn on named scheduler (SQ# addition). |
| `spawnParallel` | `Code → Handle` | Spawn on thread pool (SQ# addition). |
| `progress` | `Number → Nothing` | Report progress 0-1 from within script (SQ# addition). |
| `onProgress` | `Handle, Code → Handle` | Register progress callback (SQ# addition). |

---

## Scheduler & Execution Model

Source: https://community.bistudio.com/wiki/Scheduler

### SQF Scheduler (Original)

The SQF scheduler runs scripts cooperatively with a **3ms per frame** time budget.

| Concept | Description |
|---|---|
| **Scheduled Environment** | Scripts run in scheduler queue. Each frame, scheduler runs scripts until 3ms budget exhausted. Longest-waiting script runs first. Script paused mid-execution if budget exceeded, resumes next frame. |
| **Unscheduled Environment** | Code executes immediately in the calling context. No suspension allowed. Fast but can freeze the game. `while` loops limited to 10,000 iterations. |
| **Suspension** | `sleep`, `uiSleep`, `waitUntil` — ONLY in scheduled environment. `canSuspend` checks if suspension is allowed. |

**Where code starts scheduled**: `init.sqf`, `spawn`, `execVM`, `exec`, `call` from scheduled context.

**Where code starts unscheduled**: triggers, waypoints, event handlers, preInit functions, FSMs, `onEachFrame`, object init fields, `call` from unscheduled, `isNil { code }`, `remoteExecCall`.

### `call` vs `spawn` vs `execVM`

| Command | Environment | Returns | Suspension | Scope |
|---|---|---|---|---|
| `call code` | **Inherits** caller's environment (scheduled or unscheduled) | Last expression value | Only if already scheduled | Has access to parent's local variables and `_this` |
| `spawn code` | Always **scheduled** (new script in scheduler) | ScriptHandle | ✅ Yes | NO access to parent locals. Must pass via params. Cannot call parent's local functions. |
| `execVM "path"` | Always **scheduled** | ScriptHandle | ✅ Yes | Equivalent to `spawn compile preprocessFileLineNumbers "path"` |

**Critical**: `call` does NOT change the scheduled/unscheduled environment. `call` in scheduled → scheduled. `call` in unscheduled → unscheduled.

**Force unscheduled**: `isNil { code }` forces unscheduled execution regardless of context.

**`spawn` order NOT guaranteed**: Scripts run in scheduler priority (longest-waiting first), not spawn order. Use `BIS_fnc_spawnOrdered` if order matters.

### `compile` / `compileScript` / `preprocessFileLineNumbers`

```sqf
// String → Code (runtime compilation)
_code = compile "hint str _this;";

// File → Code (with preprocessing + #line directives)
_code = compile preprocessFileLineNumbers "script.sqf";

// File → Code (supports .sqfc bytecode too, Arma 3 2.02+)
_code = compileScript ["script.sqf", false, ""];

// File → Raw string (no preprocessing, no #line)
_string = loadFile "data.txt";

// File → Preprocessed string (with #line)
_string = preprocessFileLineNumbers "script.sqf";

// Code → String (Arma 3 2.02+)
_string = toString _code;
```

### SQ# Scheduler Design

SQ# takes the same scheduled/unscheduled model but makes it configurable and extends it for multithreading.

| Concept | SQ# Implementation |
|---|---|
| **Time budget** | Configurable per scheduler. Default 3ms. Host sets `Scheduler.TimeBudgetPerTick`. |
| **Scheduled fibers** | Each scheduler has ready/active/waiting queues. Round-robin with budget enforcement. |
| **Unscheduled execution** | `call` runs in-place, inherits current fiber. No budget limit, no suspension. |
| **Suspension** | `sleep`, `waitUntil`, `await` — suspend fiber, release to scheduler. Only in scheduled fibers. |
| **`canSuspend`** | `canSuspend` nular command → Boolean. True if current fiber is scheduled. |
| **Main scheduler** | Special "Main" scheduler for UI/Unity main thread. Pumped by host game loop. |
| **Background schedulers** | Each on own thread. Auto-pumped. For AI, physics, IO. |
| **`spawn` order** | SQ# guarantees FIFO order within same scheduler (unlike SQF). |

```
┌─────────────────────────────────────────────────────┐
│ Frame Tick (host game loop)                         │
│                                                     │
│ 1. Host pumps Main scheduler (3ms budget)           │
│    ┌──────────────────────────────────┐             │
│    │ Fiber A runs 1.2ms → yields      │             │
│    │ Fiber B runs 1.5ms → yields      │             │
│    │ Fiber C starts at 2.7ms → budget │             │
│    │ exhausted → suspended until next  │             │
│    │ frame                             │             │
│    └──────────────────────────────────┘             │
│                                                     │
│ 2. Background schedulers auto-tick (own threads)    │
│    Thread 2: AI Scheduler runs fibers               │
│    Thread 3: IO Scheduler runs fibers               │
│                                                     │
│ 3. Host renders frame                               │
│ 4. Next frame: Main scheduler resumes Fiber C       │
└─────────────────────────────────────────────────────┘
```

---

## Thread Safety — Implicit, Seamless, Zero-Burden

### Design Philosophy

Thread safety is **SQ#'s responsibility, not the scripter's**. The runtime guarantees safety by default.
A scripter writing simple single-scheduler code never encounters threading concerns.
Only when explicitly using `spawnOn`/`spawnParallel` does cross-scheduler data sharing become relevant,
and even then, the runtime provides safe primitives that prevent data races by construction.

**Three principles:**
1. **Local by default** — Everything a scripter creates belongs to the current scheduler. No sharing unless explicitly requested.
2. **Safe sharing is explicit** — To share data across schedulers, scripter uses one of three safe primitives: `Freeze`, `Channel`, or `Shared`. Each guarantees safety at the type level.
3. **VM enforces, doesn't suggest** — The runtime rejects unsafe operations. Mutating another scheduler's array throws `OwnershipError`. Calling an unsafe command from the wrong thread throws `ThreadSafetyError`.

### Ownership Model

```
┌─────────────────────────────────────────────────────────────┐
│ Scheduler "Main" (Thread 1)                                 │
│                                                             │
│  Fiber A ──owns──▶ [1, 2, 3]  ← mutable array              │
│  Fiber B ──owns──▶ { code }   ← code block                 │
│  global "SCORE" = 42          ← scheduler-local global     │
│                                                             │
│  ── .freeze() ──▶ FrozenArray([1,2,3])  ← IMMUTABLE        │
│                   can be read by ANY scheduler              │
│                                                             │
│  ── .sendTo("AI") ──▶ ownership transferred                │
│                        Fiber A loses access                 │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ Scheduler "AI" (Thread 2)                                   │
│                                                             │
│  Fiber C ──owns──▶ [4, 5, 6]  ← different mutable array    │
│  global "SCORE" = 100        ← separate! Per-scheduler      │
│                                                             │
│  Reads FrozenArray([1,2,3]) safely from any fiber           │
│  Receives array via Channel from Main                       │
└─────────────────────────────────────────────────────────────┘
```

### The Three Safe Sharing Primitives

#### 1. `Freeze` — Immutable Snapshot (Zero-Cost Read, Any Scheduler)

```sqf
// Create mutable array in current scheduler
private _data = [1, 2, 3, 4, 5];

// Freeze — becomes immutable, can be read from ANY scheduler
private _frozen = _data freeze;

// Pass frozen data to another scheduler
spawnOn "AI" {
    params ["_frozenData"];
    // Read-only access — safe from any thread
    private _sum = _frozenData select 2;  // OK
    _frozenData set [0, 99];              // RUNTIME ERROR: FrozenArray is immutable
};

// Original _data is unaffected — freeze creates snapshot
_data set [0, 99];  // Still works on original
// _frozen still [1,2,3,4,5]
```

Under the hood: `freeze` copies the array into an immutable persistent data structure. Multiple schedulers can read concurrently with zero synchronization — pure reads, no locks, no atomics. Perfect for configuration, shared lookup tables, spatial data.

#### 2. `Channel<T>` — Message Passing (Lock-Free, One-Way or Two-Way)

```sqf
// Create a channel for sending arrays between schedulers
private _channel = Channel create;  // creates Channel<Array>

// Send data to another scheduler
_channel send _data;

// In another scheduler:
private _received = _channel receive;  // blocks fiber until data arrives (cooperative)
// Or non-blocking:
if (_channel canReceive) then {
    private _received = _channel receive;
};

// Two-way: request/response pattern
private _responseChannel = Channel create;
spawnOn "AI" {
    params ["_input", "_replyTo"];
    private _result = heavyComputation(_input);
    _replyTo send _result;
} args [_data, _responseChannel];

private _result = _responseChannel receive;  // wait for AI result
```

Under the hood: Channels use lock-free SPSC (single-producer single-consumer) queues. No locks. No contention. The `receive` suspends the fiber cooperatively (not the thread) until data arrives.

#### 3. `Shared<T>` — Synchronized Mutable Value (CAS-Based, Lightweight)

```sqf
// Shared counter between schedulers
shared _counter = 0;  // atomic, initial value 0

// Any scheduler can atomically update:
spawnOn "AI" {
    _counter add 1;     // atomic increment (Interlocked.Increment)
};
spawnOn "Physics" {
    _counter add 5;     // atomic add
};

// Read current value (atomic read)
private _current = get _counter;  // always consistent (or _counter + 0)

// Compare-and-swap
_counter compareSwap [42, 99];  // if value == 42, set to 99

// Supported ops on shared Number: add, sub, compareSwap, min, max
// Supported ops on shared Boolean: compareSwap, toggle
```

Under the hood: `Shared<T>` wraps a value with CAS (compare-and-swap) operations. No locks. Uses `Interlocked` for numbers, `Volatile.Read/Write` for reads. For complex types, `Shared<T>` only supports `get`/`set` (atomic reference swap).

### Global Variables Are Scheduler-Local

```sqf
// In SQF: missionNamespace is shared by all scripts (single scheduler → no problem)
// In SQ#: each scheduler gets its OWN copy of missionNamespace

// Scheduler "Main":
global COUNTER = 0;
COUNTER = COUNTER + 1;  // COUNTER == 1

// Scheduler "AI" (different thread):
COUNTER = COUNTER + 1;  // COUNTER == 1 (its own copy!)

// To share a global across schedulers:
shared SHARED_COUNTER = 0;
// Now all schedulers see same atomic Number
```

This is the **key insight** that eliminates 90% of potential data races. Since each scheduler has its own global namespace, a scripter who never uses `spawnOn` never encounters shared state. Period.

### Host Command Thread Safety Declaration

Host registers each command with a safety level. The VM enforces at call time.

| Level | Meaning | Example |
|---|---|---|
| `Isolated` (default) | Only callable from owning scheduler | `setPos`, `setDamage`, `createVehicle` |
| `ReadOnly` | Safe from any thread | `getPos`, `getDamage`, `alive`, `count` |
| `Synchronized` | Has internal locking, safe from any thread | `setVariable`, `getVariable` |
| `MainThread` | Only callable from main/UI scheduler | UI commands, Unity API |
| `Unsafe` | No guarantees — caller beware (advanced hosts only) | Direct memory access |

```csharp
host.RegisterUnary("getPos", obj => obj.Position, 
    threadSafety: ThreadSafety.ReadOnly);

host.RegisterBinary("setPos", (obj, pos) => { obj.Position = pos; return SqValue.Nil; },
    threadSafety: ThreadSafety.Isolated);
```

When a fiber on scheduler "AI" calls `setPos` and that command is `Isolated` to scheduler "Main":
→ `ThreadSafetyError: Command 'setPos' is isolated to scheduler 'Main'. Current scheduler is 'AI'.`

### Array and Compound Type Safety

| Operation | Rule | Violation |
|---|---|---|
| Create array | Owned by creating fiber's scheduler | — |
| Read array | Any scheduler can read its OWN arrays | Reading another scheduler's mutable array → `OwnershipError` |
| Mutate array | Only owning scheduler can mutate | Cross-scheduler mutation → `OwnershipError` |
| `_arr freeze` | Creates immutable snapshot — any scheduler can read | Mutation attempt → `ImmutableError` |
| `_arr sendTo(scheduler)` | Transfers ownership — source loses access | Access after transfer → `OwnershipError` |
| `_arr copy` | Creates new owned copy in current scheduler | — |

### What the Scripter Sees (Summary)

```
┌──────────────────────────────────────────────────────────────┐
│  SCRIPTER EXPERIENCE                                         │
│                                                              │
│  Single scheduler (99% of scripts):                          │
│    • Write normal SQF-style code                             │
│    • No threading concerns. No locks. No atomics.            │
│    • Everything just works.                                  │
│                                                              │
│  Multi-scheduler (opt-in):                                   │
│    • spawnOn "AI" { ... } — run on AI thread                │
│    • Freeze data before sharing → immutable, safe            │
│    • Channel for message passing → safe, lock-free           │
│    • Shared<T> for counters/flags → safe, CAS-based          │
│    • VM rejects unsafe ops → clear error, no silent corrupt  │
│                                                              │
│  NEVER:                                                      │
│    • No locks to manage                                      │
│    • No mutexes to acquire                                   │
│    • No deadlocks possible (channels are lock-free)          │
│    • No data races possible (ownership + freeze + channel)   │
│    • No silent memory corruption                             │
└──────────────────────────────────────────────────────────────┘
```

### Thread Safety Commands (StdLib)

| Command | Signature | Description |
|---|---|---|
| `freeze` | `Any → Frozen<T>` | Create immutable snapshot. Readable from any scheduler. |
| `thaw` | `Frozen<T> → T` | Create mutable copy in current scheduler. |
| `sendTo` | `[Any, Scheduler] → Nothing` | Transfer ownership to scheduler. Source loses access. |
| `copy` | `Any → T` | Create new owned copy in current scheduler (always safe). |
| `Channel create` | `→ Channel<T>` | Create lock-free message channel. |
| `Channel send` | `[Channel<T>, T] → Nothing` | Send value through channel. |
| `Channel receive` | `Channel<T> → T` | Receive value (suspends fiber). |
| `Channel canReceive` | `Channel<T> → Boolean` | Check if data available (non-blocking). |
| `shared` | `T → Nothing` | Declare shared (atomic) variable. Like `private` but CAS-based. |
| `Shared get` | `Shared<T> → T` | Atomic read. |
| `Shared set` | `[Shared<T>, T] → Nothing` | Atomic write. |
| `Shared add` | `[Shared<Number>, Number] → Nothing` | Atomic increment (Number only). |
| `Shared sub` | `[Shared<Number>, Number] → Nothing` | Atomic decrement (Number only). |
| `Shared compareSwap` | `[Shared<T>, T, T] → Boolean` | CAS: if current == expected, set to new. Returns success. |
| `isFrozen` | `Any → Boolean` | Check if value is immutable/frozen. |
| `owner` | `Any → Scheduler` | Get owning scheduler of a value. |

### Implementation Notes

- **Ownership tracking**: Each `SqArray`, `SqHashMap`, `SqCode` carries a `SchedulerId` (8 bytes). Checked on mutation.
- **Freeze**: Uses `System.Collections.Immutable` for arrays, custom persistent structures for HashMap. Copy cost amortized over reads.
- **Channel**: `System.Threading.Channels` or custom lock-free SPSC queue. Fiber suspension via `TaskCompletionSource`-like mechanism.
- **Shared<T>**: Thin wrapper over `Interlocked`/`Volatile`. No heap allocation beyond the wrapper object.
- **Global partition**: Each scheduler has its own `Dictionary<string, SqValue>` for globals. No sharing unless `Shared<T>`.
- **Performance**: Zero overhead for single-scheduler use (no atomics, no locks, no indirection). Overhead only when crossing scheduler boundaries.

---

## Magic Variables

Source: https://community.bistudio.com/wiki/Magic_Variables

Runtime-provided variables scoped to specific execution contexts.

### Core (Available in All SQ# Scripts)

| Variable | Context | Value |
|---|---|---|
| `_this` | Any called/spawned code, event handlers | Arguments passed to the code block |
| `_x` | `forEach`, `count`, `select`, `apply`, `findIf` | Current element being processed |
| `_y` | `forEach` on HashMap | Current value (key is `_x`) |
| `_forEachIndex` | `forEach` loops | Zero-based index of current element |
| `_thisScript` | Inside `spawn`ed/`execVM`ed script | Own ScriptHandle |
| `_exception` | `catch` block | Caught exception value |

### Host-Defined (Available When Host Registers Them)

| Variable | Context | Value |
|---|---|---|
| `this` | Object init, triggers, waypoints, dialogs | Context-dependent (object, group leader, etc.) |
| `_thisEvent` | Event handlers | Event name string |
| `_thisEventHandler` | Event handlers | Handler index |
| `_thisFSM` | FSM scripts | FSM handle |
| `_self` | HashMapObject methods | The HashMapObject instance |

### SQ# Additions

| Variable | Context | Value |
|---|---|---|
| `_error` | `catch` block | Structured error object (SQ# addition — richer than `_exception`) |
| `_scheduler` | Any fiber | Name of current scheduler (SQ# addition) |
| `_fiberId` | Any fiber | Unique fiber identifier (SQ# addition) |

---

## String Semantics

Source: https://community.bistudio.com/wiki/String

### SQF String Properties

| Property | Value |
|---|---|
| **Max length** | 9,999,999 ~ 10,000,000 chars (Arma 3 1.56+). Was 2,056 in OFP. |
| **Encoding** | UTF-8 |
| **Quote types** | `"double"` and `'single'` (since Arma 1) |
| **Escape sequences** | **None.** No `\n`, `\t`, `\\`. Use `toString [10]` for newline, `endl` constant. |
| **Quote inside quote** | `"say ""hello"""` or `'say "hello"'` |
| **Preprocessor interaction** | Single-quoted strings ARE parsed by preprocessor (macros expanded). Double-quoted strings are NOT. |
| **Only operator** | `+` for concatenation |

### Key String Commands

| Command | Signature | Description |
|---|---|---|
| `format` | `[String, Any...] → String` | sprintf-style: `format ["HP: %1/%2", _hp, _maxHp]` |
| `str` | `Any → String` | Any value to string |
| `parseNumber` | `String → Number` | String to number |
| `toString` | `Array → String` | Char codes to string: `toString [72, 105]` → `"Hi"` |
| `toArray` | `String → Array` | String to char codes |
| `count` | `String → Number` | String length (char count) |
| `select` | `[String, Number] → String` | Char at index (returns single-char string) |
| `in` | `[String, String] → Boolean` | Substring check: `"ello" in "Hello"` |
| `find` | `[String, String] → Number` | Index of substring, -1 if not found |
| `splitString` | `[String, String] → Array` | Split by delimiter |
| `joinString` | `[Array, String] → String` | Join array with delimiter |
| `toLower` / `toUpper` | `String → String` | Case conversion |
| `trim` | `String → String` | Remove leading/trailing whitespace |

### SQ# String Enhancements

| Feature | SQ# |
|---|---|
| **Escape sequences** | `\n`, `\t`, `\\`, `\"`, `\'` supported |
| **String interpolation** | `f"Hello {_name}, HP: {_hp}/{_maxHp}"` — compiled to `format` call |
| **Verbatim strings** | `@"C:\path\to\file"` — no escape processing |
| **Multi-line strings** | `""" ... """` triple-quote syntax |
| **Unicode** | Full UTF-16 support, `\uXXXX` escapes |
| **Regular expressions** | `_str =~ /pattern/` operator (SQ# addition) |

---

## Namespace Semantics

Source: https://community.bistudio.com/wiki/Namespace

Namespaces are key-value containers for global variables. Every global variable lives in exactly one namespace.

### SQF Namespaces

| Namespace | Since | Lifetime | Serialized? |
|---|---|---|---|
| `missionNamespace` | Arma 2 1.00 | Mission duration | ✅ (saveGame) |
| `parsingNamespace` | Arma 2 1.00 | Game session | ❌ |
| `uiNamespace` | Arma 2 1.00 | Game session | ❌ |
| `profileNamespace` | TKOH 1.00 | Profile lifetime | ✅ (profile) |
| `localNamespace` | Arma 3 2.00 | Mission duration | ❌ (not networked) |
| `serverNamespace` | Arma 3 2.06 | Game session | ❌ (server only) |
| `missionProfileNamespace` | Arma 3 2.10 | Profile lifetime | ✅ (scoped to mission) |

### Key Operations

```sqf
// Get/set variables in namespaces
missionNamespace setVariable ["myVar", 42];
_value = missionNamespace getVariable ["myVar", 0];  // 0 = default

// Switch context with 'with'
with uiNamespace do {
    myCtrlVar = 5;  // sets uiNamespace variable
};

// List all variables
_vars = allVariables missionNamespace;

// Current namespace
_ns = currentNamespace;
```

### SQ# Namespace Model

SQ# replaces SQF's flat global namespace system with a **hybrid** model:

| Concept | SQ# |
|---|---|
| **Module scope** | Each `import`ed file has its own module scope. Variables are module-private by default. |
| **`global` keyword** | `global SERVER_FPS = 60;` — explicit global in the module's chosen namespace. |
| **Namespaces** | `Namespace` type still exists. `missionNamespace`, `uiNamespace` etc. registered by host. |
| **`getVariable`/`setVariable`** | Same API: `_ns setVariable ["key", value]`. `_ns getVariable "key"`. |
| **`with`/`do`** | `with _ns { ... }` — switch namespace context for block. Same as SQF. |
| **`currentNamespace`** | Nular command. Returns namespace current code runs in. |

---

## `params` / `param` Validation

Source: https://community.bistudio.com/wiki/params

```sqf
// Basic unpack
[1, 2, 3] call {
    params ["_one", "_two", "_three"];
};

// With defaults and type/size validation
params [
    "_name",                                    // required
    ["_age", 0, [0]],                          // default 0, must be Number
    ["_tags", [], [[]], [2, 4]],               // default [], must be Array, 2-4 elements
    ["_optional", nil]                          // default nil, any type
];

// params returns false if any default was used
if (!params ["_req1", ["_opt", 42]]) exitWith {
    hint "Using defaults!";
};

// Skip elements
params ["", "", "_onlyThird"];  // skip first two

// Non-array argument auto-wraps
123 call { params ["_x"]; };  // _x = 123 (auto-wrapped to [123])
```

### SQ# `params` Enhancements

```sqf
// Type annotations in params
params [
    _name: string,
    _age: int = 0,
    _tags: string[] = []
];

// Destructuring
params [_x, _y, _z]: Vector3;

// Rest params
params [_first, ..._rest];
```

---

## `isEqualTo` vs `==`

Source: https://community.bistudio.com/wiki/isEqualTo

| Behavior | `==` | `isEqualTo` |
|---|---|---|
| **Type coercion** | Yes (quirky) | No — strict |
| **String comparison** | Case-insensitive in some contexts | Case-sensitive |
| **Different types** | May error | Returns false (no error) |
| **Arrays** | ❌ (error) | ✅ Deep comparison |
| **nil comparison** | Error-prone | `nil isEqualTo x` → **Nothing** (SQF bug/quirk) |
| **Namespaces, ScriptHandles** | ❌ | ✅ |
| **Performance** | Slower | Faster |
| **Booleans** | `alive player == true` — error before Arma 3 2.00 | `alive player isEqualTo true` — fine |

**SQ# approach**: `==` is strict (like `isEqualTo`). `isEqualTo` kept as alias. No type coercion unless explicit.
`nil == nil` → `true`. `nil == anything` → `false`.

---

## Preprocessor (Legacy Opt-In)

Source: https://community.bistudio.com/wiki/PreProcessor_Commands

SQ# provides an opt-in legacy preprocessor that supports the full SQF preprocessor syntax for compatibility.

| Directive | Support |
|---|---|
| `#define MACRO`, `#define MACRO(args)` | ✅ Including variadic (`...`, `__VA_ARGS__`, `__VA_OPT__`, `__VA_APPLY__`, `__VA_SELECT__`) |
| `#undef` | ✅ |
| `#if`, `#ifdef`, `#ifndef`, `#else`, `#endif` | ✅ (including nested `#ifdef` — SQF bug: SQF can't nest these) |
| `#include "path"` | ✅ |
| `#` (stringify), `##` (concat) | ✅ |
| `__LINE__`, `__FILE__`, `__FILE_NAME__`, `__FILE_SHORT__` | ✅ |
| `__has_include` | ✅ |
| `__DATE_ARR__`, `__DATE_STR__`, `__DATE_STR_ISO8601__` | ✅ |
| `__TIME__`, `__TIME_UTC__`, `__DAY__`, `__MONTH__`, `__YEAR__` | ✅ |
| `__TIMESTAMP_UTC__` | ✅ |
| `__COUNTER__`, `__COUNTER_RESET__` | ✅ |
| `__RAND_INT8__` through `__RAND_INT64__` | ✅ |
| `__GAME_VER__`, `__GAME_VER_MAJ__`, `__GAME_VER_MIN__`, `__GAME_BUILD__` | ✅ (host-defined values) |
| `__EXEC`, `__EVAL` | ❌ (Config Parser only — not applicable to SQ#) |

**Usage**: `sqf run --preprocess legacy.sqf` or `#pragma preprocessor legacy` at top of file.

SQ# adds its own preprocessor directives (always available):
| Directive | Description |
|---|---|
| `#pragma sqsharp strict` | Enable strict mode |
| `#pragma sqsharp compat` | Enable SQF compatibility mode |
| `#pragma preprocessor legacy` | Enable legacy preprocessor for this file |
| `#pragma scheduler "name"` | Default scheduler for spawn in this file |

---

### Keep from SQF
- **Operator-first design**: nular/unary/binary model preserved as core paradigm
- **Control structures are operators** chained by helper types
- `call` / `spawn` / `execVM` semantics (call inherits env, spawn always scheduled)
- **Scheduler model**: scheduled (3ms budget) / unscheduled environments
- `compile`, `compileScript`, `loadFile`, `preprocessFileLineNumbers` for runtime code loading
- Code-as-data (`{}` blocks are first-class values)
- Greedy unary operator consumption
- Full precedence table (levels 11→1)
- Assignment by reference for arrays, unary `+` for deep copy
- `forEach`, `select`, `apply`, `findIf`, `arrayIntersect` array ops
- `switch`, `if/else`, `while`, `for`/`from`/`to`/`step` control flow
- `params`/`param` with type and size validation
- `private` scoping (both `private _x = v` and `private "_x"` forms)
- `nil` sentinel (maps to `Nothing` in SQ# — but storable, not deletive)
- Magic variables: `_this`, `_x`, `_y`, `_forEachIndex`, `_thisScript`, `_exception`
- Namespaces: `missionNamespace`, `uiNamespace`, `profileNamespace`, `parsingNamespace`
- `with`/`do` namespace context switching
- `getVariable`/`setVariable` on namespaces
- `isEqualTo` strict comparison (`==` aliased, no type coercion)
- `format`, `str`, `toString`, `toArray`, `parseNumber` string ops
- `;` statement terminator
- Legacy preprocessor available as opt-in (`#define`, `#include`, `#ifdef`, etc.)

### Add (SQ# modernizations)
- **Clean syntax sugar** for if/while/for/switch (parsed as sugar, lowered to operator chains)
- **Scheduler enhancements**: Named schedulers, configurable time budget, FIFO spawn order, multi-thread schedulers
- Optional static type annotations (`private _x: int = 5`)
- Better error messages with source locations
- Module/import system (`import "utils.sqf"` instead of `#include`)
- Explicit `global` keyword (no implicit global leak)
- String interpolation (`f"Hello {_name}"` — compiles to `format` call)
- String escape sequences (`\n`, `\t`, `\\`, `\"`, `\'`, `\uXXXX`)
- Verbatim strings (`@"C:\path"`) and multi-line strings (`"""..."""`)
- Enums, simple structs
- `try`/`catch` for error handling (with structured `_error` variable)
- Debugger hooks (breakpoints, step, watch)
- `params` with type annotations and destructuring
- `==` is strict (like `isEqualTo`), no type coercion
- `nil` is storable value (not deletive like SQF)
- `undefine` keyword for explicit variable deletion
- Promise combinators: `PromiseAll`, `PromiseRace`, `PromiseAny`
- `spawnOn` / `spawnParallel` for scheduler-aware and thread-pool spawning
- **Implicit thread safety**: Scheduler-local globals, ownership tracking, `Freeze`/`Channel`/`Shared` primitives
- Zero data races by design — VM rejects unsafe cross-scheduler operations
- `await` keyword for fiber-suspending async
- Progress reporting (`progress` / `onProgress`)
- Regular expressions (`_str =~ /pattern/`)
- Magic variables: `_error`, `_scheduler`, `_fiberId` (SQ# additions)
- `#pragma` directives for file-level configuration
- Configurable array size limit (host-defined, no hard cap)

### Drop
- `#define` / `#ifdef` preprocessor (opt-in legacy mode only)
- `config.cpp` class syntax (host provides own config)
- Arma-specific types (`side`, `team`, `group`, `objNull` — unless host registers)
- Legacy `comment` operator (actually executes — waste)
- SQS syntax (already deprecated in Arma 3)
- **nil-deletes-variable behavior** — `_var = nil` stores nil, doesn't delete (use `undefine`)
- **Single-quote preprocessor parsing** — SQ# treats `'` and `"` identically (no preprocessor inside strings)
- **`spawn` non-deterministic order** — SQ# guarantees FIFO within scheduler
- **Nested `#ifdef` limitation** — SQ# preprocessor supports nesting
- **`==` type coercion** — SQ# `==` is strict (`isEqualTo` semantics)
- **Index rounding (banker's)** — SQ# uses truncation for float→int index, or requires `int`

---

## Parser Design (Pratt / Precedence Climbing)

### Architecture
```
Source → Lexer → Token Stream → Pratt Parser → AST
                                      ↑
                              Precedence Table
                              (host-extensible)
```

### Token Types
```
IDENTIFIER, NUMBER, STRING, CODE_BLOCK  // values
PLUS, MINUS, STAR, SLASH, PERCENT, CARET  // arithmetic
EQ, NEQ, LT, GT, LTE, GTE  // comparisons
AND, OR, NOT, BANG  // logical
HASH, COLON  // hash-select, switch-case
SEMICOLON, COMMA  // terminators
LPAREN, RPAREN, LBRACKET, RBRACKET  // brackets
ASSIGN  // =
```

### Pratt Parser Binding Powers

```csharp
// Precedence → binding power mapping for Pratt parser
// Higher number = tighter binding
static class Precedence
{
    public const int None = 0;           // lowest (not used)
    public const int LogicalOr = 1;      // ||, or
    public const int LogicalAnd = 2;     // &&, and
    public const int Comparison = 3;     // ==, !=, <, >, <=, >=
    public const int BinaryCommand = 4;  // select, set, resize, switch case :
    public const int Else = 5;           // else
    public const int AddSub = 6;         // +, -, min, max
    public const int MulDiv = 7;         // *, /, %, mod, atan2
    public const int Power = 8;          // ^
    public const int HashSelect = 9;     // #
    public const int Unary = 10;         // prefix +, -, !, not, unary commands
    public const int Nular = 11;         // variables, literals, brackets
}
```

### Parse Flow

1. **Prefix position**: parse nular (variable, literal, `(`, `[`, `{`) or unary operator
2. **Infix position**: while next token's precedence > current precedence, parse as binary operator
3. **Unary**: nular identity acts as prefix handler — returns compiled value
4. **Binary**: infix handler — takes left expression, parses right at same precedence, returns binary node

### Control Structure Desugaring

Parser recognizes control flow patterns and desugars to operator chains:

```
Input:                          AST lowered to:
if (cond) { a } else { b }   → if(cond) .then({a}) .else({b})
while { cond } { body }       → while({cond}) .do({body})
for "_i" from 0 to 9 { ... } → for("_i") .from(0) .to(9) .do({...})
switch (val) { case 1: ... } → switch(val) .do({ case(1): {...} })
```

This gives users clean syntax while preserving the operator-chain extensibility model.

---

## Architecture Pipeline
```
Source (.sqf) → [Legacy Preprocessor opt-in] → Lexer → Tokens → Pratt Parser → AST
    → Semantic Analyzer → IR → Bytecode Compiler → Bytecode → VM → Scheduler → Host API
```

---

## Bytecode VM Design

### Instruction Set

**Value operations:**
```
PUSH_CONST <idx>         ; push constant from pool
PUSH_LOCAL <idx>         ; push local var by index
STORE_LOCAL <idx>        ; pop → store to local
PUSH_GLOBAL <nameId>     ; push global variable
STORE_GLOBAL <nameId>    ; pop → store to global
```

**Arity-dispatched host commands:**
```
NULAR_CALL <cmdId>       ; call nular command → push result
UNARY_CALL <cmdId>       ; pop 1 arg → call → push result
BINARY_CALL <cmdId>      ; pop right, pop left → call → push result
```

**Code and arrays:**
```
MAKE_CODE <addr>         ; push code reference pointing to bytecode offset
MAKE_ARRAY <count>       ; pop count items → push array
MAKE_HASHMAP             ; create empty hashmap → push
```

**Control flow:**
```
JUMP <offset>            ; unconditional branch
JUMP_IF_FALSE <offset>   ; pop, jump if false
JUMP_IF_TRUE <offset>    ; pop, jump if true
CALL <argc>              ; call code value with argc args (stack args + code ref)
SPAWN <argc>             ; spawn new fiber executing code value
RET                      ; return from call / fiber end
YIELD                    ; yield fiber back to scheduler
```

**Stack:**
```
DUP                      ; duplicate top
POP                      ; discard top
SWAP                     ; swap top two
```

### Compiler: Operator → Bytecode

Arithmetic/logical operators compile to `BINARY_CALL` or `UNARY_CALL` with well-known command IDs,
allowing inlining optimization later:

```
a + b      → PUSH_LOCAL a, PUSH_LOCAL b, BINARY_CALL <add_id>
!cond      → PUSH_LOCAL cond, UNARY_CALL <not_id>
arr select 0 → PUSH_LOCAL arr, PUSH_CONST 0, BINARY_CALL <select_id>
```

Assignment compiles specially:
```
_a = expr  → [expr bytecode], STORE_LOCAL <_a_idx>, DUP
```

---

## Project Structure
```
SQF.NET/
├── src/
│   ├── SQSharp.Core/            # SqValue, SqType, core abstractions
│   ├── SQSharp.Language/        # Lexer, Pratt Parser, AST definitions
│   ├── SQSharp.Compiler/        # AST → IR → Bytecode compiler
│   ├── SQSharp.VM/              # Stack VM, instruction dispatch
│   ├── SQSharp.Scheduler/       # Fiber engine, cooperative scheduling
│   ├── SQSharp.Host/            # IHost, command registration by arity+precedence
│   ├── SQSharp.StdLib/          # Standard commands (math, string, array, logic)
│   ├── SQSharp.CLI/             # dotnet sqf run/repl/compile/serve
│   └── SQSharp.Preprocessor/    # Opt-in legacy #define + comment stripping
├── tests/
│   ├── SQSharp.Core.Tests/
│   ├── SQSharp.Language.Tests/  # Parser/precedence tests
│   ├── SQSharp.Compiler.Tests/
│   ├── SQSharp.VM.Tests/
│   └── SQSharp.Integration.Tests/
├── samples/
│   ├── HostMinimal/
│   └── HostGame/
└── docs/
    └── lang-spec.md
```

---

## Host API

```csharp
public interface ISqHost
{
    // Register commands by arity + precedence for disambiguation
    void RegisterNular(string name, Func<SqValue> handler);
    void RegisterUnary(string name, Func<SqValue, SqValue> handler);
    void RegisterBinary(string name, Func<SqValue, SqValue, SqValue> handler, int precedence);
    
    // Register a type that can be returned by commands
    void RegisterType<T>(string typeName) where T : class;
    
    // Resolve unknown names (enables dynamic/late-bound commands)
    SqValue? ResolveNular(string name);
    SqValue? ResolveUnary(string name, SqValue arg);
    SqValue? ResolveBinary(string name, SqValue left, SqValue right);
    
    // Lifecycle callbacks
    void OnScriptStart(SqFiber fiber);
    void OnScriptEnd(SqFiber fiber, SqValue? result);
    void OnError(SqFiber fiber, SqError error);
    void OnPrint(string message, SqPrintChannel channel);
    
    // Time (for sleep, diag_tickTime)
    double CurrentTime { get; }
}
```

---

## NuGet Packages
| Package | Role |
|---|---|
| `SQSharp.Core` | SqValue, type system, zero deps |
| `SQSharp.Runtime` | Core + VM + scheduler |
| `SQSharp.Compiler` | Lexer + Pratt parser + bytecode compiler |
| `SQSharp.CLI` | dotnet tool (run, repl, compile) |
| `SQSharp.Hosting` | Host abstractions + std library commands |
| `SQSharp.Unity` | Unity MonoBehaviour host, coroutine bridge (🏠 separate package) |

---

## Milestones
### M1 — Full Language + Host Demo
- Lexer + Pratt parser with full precedence table (levels 1-11)
- Control structure desugaring (if/while/for/switch)
- Semantic analyzer (scoping, basic type check)
- Bytecode compiler (all constructs, arity-dispatched commands)
- Stack VM executing bytecode
- Cooperative fiber scheduler (single-thread first)
- Host API with arity+precedence registration
- 50+ standard commands (math, string, array, logic, control flow)
- Sample game-like host demo
- CLI: `sqf run`, `sqf repl`, `sqf compile`

### M2 — Polish + Tooling
- Binary `.sqfc` bytecode serialization
- Opt-in legacy preprocessor pass
- Multi-thread schedulers
- Debug Adapter Protocol support
- VS Code extension (syntax highlighting, debugging)
- Performance benchmarks + VM optimization (inlining, superinstructions)

### M3 — Ecosystem
- Unity integration package
- Documentation site with language spec
- Project templates (classlib, console, unity)
- Community samples repository

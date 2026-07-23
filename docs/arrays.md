# Array Semantics

> Aligned with [Bohemia Interactive wiki](https://community.bistudio.com/wiki/Array)

## SQF Array Properties

| Property | Value |
|---|---|
| **Types** | Heterogeneous — any type in same array |
| **Size limit** | 9,999,999 (some 10,000,000) since Arma 3 v1.56 |
| **Storage** | Reference type. `_b = _a` shares same array. |
| **Indexing** | Zero-based. `_arr select 0` or `_arr # 0` (Arma 3). |
| **Trailing comma** | NOT allowed. `[1,2,]` → error. |
| **Read-only** | Some arrays frozen. Must copy with `+` before mutating. |

## Index Behavior (SQF Quirks)

Array `["element0"]` (count = 1):

```sqf
_arr select -1;  // Error Zero Divisor       — negative = always error
_arr select  0;  // "element0"               — valid
_arr select  1;  // nil                      — exactly count: soft OOB
_arr select  2;  // Error Zero Divisor       — > count: hard error
```

**Only exactly ONE past end returns nil.** Inconsistent and bug-prone.

**Index rounding**: Floats use banker's rounding (X.5 → nearest even):
```
-0.5 → 0    0.5 → 0    1.5 → 2    2.5 → 2
```

**param** — safe alternative:
```sqf
_arr param [5];         // nil (no error)
_arr param [5, "abc"];  // "abc" (default)
```

## set — Auto-Resize

| Index | Behavior |
|---|---|
| In range | Replace element |
| Positive OOB | Auto-resize. Gaps filled with nil. |
| Negative | Error Zero Divisor |

```sqf
_arr = ["a"];
_arr set [3, "d"];  // ["a", nil, nil, "d"]
```

## resize

```sqf
_arr resize 3;  // truncate
_arr resize 5;  // expand with nil fill
```

## Mutation vs Copy

| Operation | Mutates? | Returns |
|---|---|---|
| `set [i, v]` | ✅ | Nothing |
| `pushBack v` | ✅ | Index |
| `append [a,b]` | ✅ | Nothing |
| `deleteAt i` | ✅ | Deleted element |
| `deleteRange [i,n]` | ✅ | Nothing |
| `resize n` | ✅ | Nothing |
| `sort asc` | ✅ | Nothing |
| `reverse` | ✅ | Nothing |
| `+ [x]` | ❌ copy | New array |
| `- [x]` | ❌ copy | New array |
| `+_arr` (unary) | ❌ copy | New array (**deep** copy of sub-arrays!) |
| `apply { ... }` | ❌ copy | New array |
| `select { cond }` | ❌ copy | New array |
| `arrayIntersect` | ❌ copy | New array |

### Critical: `+_arr` is DEEP copy

```sqf
_a = [[1,2], [3,4]];
_b = +_a;
(_a select 0) set [0, 99];  // _b[0] still [1,2] — deep copy!
```

## Array Subtraction

Removes **ALL** occurrences:
```sqf
["a","b","c","a","b","c"] - ["a","b"];  // ["c","c"]
```

Single instance: use `deleteAt` with `find`.

## Functional Operations

```sqf
// apply — map (new array)
[1,2,3] apply { _x * 2 };           // [2,4,6]

// select with code — filter (new array)
[1,2,3,4,5] select { _x > 3 };      // [4,5]

// findIf — first match index (stops early)
[1,2,3,4,5] findIf { _x == 3 };     // 2

// arrayIntersect — also removes duplicates!
[1,2,2,3,4] arrayIntersect [1,2,2,3,4];  // [1,2,3,4]
```

## Sorting

```sqf
_arr sort true;    // ascending
_arr sort false;   // descending
// Works on strings, numbers, sub-arrays (sorts by first element)
reverse _arr;      // in-place
```

---

## SQ# Array Design — Cleaned Up

| Issue | SQF | SQ# |
|---|---|---|
| **select OOB** | -1→error, count→nil, count+1→error | `_arr[idx]` → nil for any OOB |
| **Index rounding** | Banker's rounding | Float→banker's (compat). int→exact. |
| **set OOB** | Auto-resize | Kept. `_arr[i] = v` sugar. |
| **nil in arrays** | nil = deleted variable | `nil` as value OK. `[1, nil, 3]` valid. Storing nil in variable deletes it. |
| **Copy depth** | `+_arr` deep | `copy` shallow, `deepCopy` recursive, `+_arr` = deepCopy alias. |
| **Subtract all** | `_arr - [x]` all | Same. `removeFirst x` for single. |
| **Trailing comma** | Error | Allowed: `[1, 2, 3,]` |
| **Read-only** | Error | `FrozenArray`. `freeze()` / `thaw()`. |
| **Size limit** | ~10M | No hard cap. Host-configurable. |
| **forEach** | `{...} forEach _arr` | Same + `for (x in _arr) { ... }` |
| **Hash-select** | `_arr # idx` | `_arr[idx]` preferred. `#` supported. |

## SQ# Array Usage

```sqf
private _arr = [1, 2, 3, 4, 5];

// Access
_arr[0];              // 1
_arr[99];             // nil (safe OOB)
_arr select 0;        // 1 (legacy)

// Mutation
_arr pushBack 6;      // [1,2,3,4,5,6]
_arr append [7, 8];   // [1,2,3,4,5,6,7,8]
_arr deleteAt 0;      // [2,3,4,5,6,7,8]
_arr[0] = 99;         // bracket assignment
_arr set [1, 100];    // legacy

// Functional (return new arrays)
_arr apply { _x * 2 };
_arr select { _x > 4 };
_arr findIf { _x == 5 };

// Copy
_arr2 = _arr copy;              // shallow
_arr2 = _arr deepCopy;          // recursive
_arr2 = +_arr;                  // deepCopy alias (SQF compat)

// Freeze / thaw
private _frozen = _arr freeze;
_frozen[0] = 5;                 // ERROR: frozen
private _thawed = _frozen thaw; // new mutable copy
```

## Standard Array Commands (StdLib)

| Category | Commands |
|---|---|
| **Access** | `select`, `#`, `param`, `params` |
| **Mutation** | `set`, `pushBack`, `pushBackUnique`, `append`, `deleteAt`, `deleteRange`, `insert` |
| **Size** | `count`, `resize`, `isEmpty` |
| **Search** | `find`, `findIf`, `in`, `arrayIntersect` |
| **Functional** | `apply`, `select` (filter), `reduce`, `all`, `any` |
| **Sort** | `sort`, `reverse`, `shuffle` |
| **Copy** | `+` (unary=deepCopy), `copy`, `deepCopy`, `freeze`, `thaw` |
| **Set ops** | `+` (concat), `-` (remove all), `arrayUnion` |

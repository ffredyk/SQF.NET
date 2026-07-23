# SQ# Code Optimisation

> Performance tips for SQ# scripts. Based on Arma 3 Code Optimisation wiki,
> adapted for SQ#'s bytecode VM and scheduler model.

## The Three Rules

1. **Make it work** — Get correct behavior first.
2. **Make it readable** — Clean names, consistent format.
3. **Optimise then** — Only after it works and is readable.

## SQ#-Specific Optimizations

### ✅ String Building

```sqf
// SLOW — each + creates a new string copy:
_msg = "";
for "_i" from 0 to 999 do {
    _msg = _msg + "x";       // 1000 allocations!
};

// FAST — build array, join once:
_parts = [];
for "_i" from 0 to 999 do {
    _parts pushBack "x";
};
_msg = joinString [_parts, ""];  // 1 allocation
```

### ✅ Array Building

```sqf
// FAST — mutate in place:
_arr pushBack _item;        // O(1) amortized
_arr append _otherArray;    // O(n) bulk

// SLOW — creates new array every time:
_arr = _arr + [_item];      // O(n) copy
```

### ✅ Loop Optimization

```sqf
// FAST — for-from-to (native loop):
for "_i" from 0 to (count _arr - 1) do {
    private _x = _arr select _i;
    process(_x);
};

// FAST — forEach (when _x needed):
{ process(_x) } forEach _arr;

// SLOW — while with count every iteration:
_i = 0;
while { _i < count _arr } do {  // count called every loop!
    private _x = _arr select _i;
    process(_x);
    _i = _i + 1;
};

// FAST — cache the count:
_i = 0;
_count = count _arr;
while { _i < _count } do {
    private _x = _arr select _i;
    process(_x);
    _i = _i + 1;
};
```

### ✅ Early Exit

```sqf
// SLOW — findIf stops early, count doesn't:
{ _x == target } count _arr > 0;     // checks ALL elements

// FAST — stops at first match:
(_arr findIf { _x == target }) != -1; // stops early
```

### ✅ Global Variable Access

```sqf
// SLOW — global lookup every iteration:
for "_i" from 0 to 99 do {
    _val = SOME_GLOBAL select 0;  // global dict lookup each time
};

// FAST — copy to local once:
private _val = SOME_GLOBAL select 0;
for "_i" from 0 to 99 do {
    process(_val);                  // local access
};
```

### ✅ Scheduled vs Unscheduled

```sqf
// Use call for time-critical code (no scheduler overhead):
_result = _args call { heavyComputation(_this) };

// Use spawn for long-running/background work:
_handle = _data spawn { sleep 5; process(_this); };

// Don't spawn per-unit — spawn once with array:
{ _x spawn _code; } forEach _units;  // BAD — N scripts
_units spawn _code;                   // GOOD — 1 script, loop inside
```

### ✅ execVM Caching

```sqf
// BAD — recompiles file every call:
for "_i" from 0 to 99 do {
    execVM "process.sqf";       // file read + compile × 100!
};

// GOOD — compile once, call many times:
private _fnc = compile preprocessFileLineNumbers "process.sqf";
for "_i" from 0 to 99 do {
    _i call _fnc;               // just execution
};

// BEST — declare as function (pre-compiled):
for "_i" from 0 to 99 do {
    _i call TAG_fnc_process;    // CfgFunctions pre-compiled
};
```

### ✅ Thread Safety

```sqf
// Sharing data between schedulers:
_data = [1, 2, 3, 4, 5];

// READ-ONLY sharing: freeze + pass as arg
_frozen = freeze _data;
[_frozen] spawnOn ["AI", {
    params ["_d"];           // immutable, zero-cost reads
    _sum = _d select 0;
}];

// MUTABLE sharing: use shared command for atomic vars
shared _counter = 0;
spawnOn ["AI", { _counter add 1; }];  // atomic, lock-free
_val = get _counter;         // explicit atomic read
```

## Avoid

| Pattern | Why | Alternative |
|---|---|---|
| `while { true }` without `sleep` | Scheduler hogs 3ms budget every frame | `while { sleep 0.01; condition }` |
| `execVM` in loops | File I/O + recompile every iteration | `compile` once, `call`/`spawn` many times |
| `_arr = _arr + [x]` in loops | O(n) copy per iteration | `pushBack` |
| `count _arr` in while condition | Recalculated every iteration | Cache in variable |
| Deeply nested if/else | Hadouken code, hard to read | Early return with `exitWith` |
| Globals without TAG_ prefix | Namespace collision with other mods | `TAG_variableName` |

## Scheduled Environment

SQ# scripts in scheduled environment (spawned) get ~3ms per frame. Tips:

- Break long loops with `sleep 0.01` to yield.
- Use `call` for time-critical work (runs unscheduled, no budget limit).
- Check `canSuspend` before using `sleep`/`waitUntil`/`await`.

## SQ# vs SQF Performance Notes

| SQF | SQ# | Note |
|---|---|---|
| Lazy eval `a && {b}` | 🔜 Future | Currently evaluates both sides |
| `isEqualTo` is faster than `==` | `==` IS `isEqualTo` | No difference in SQ# |
| `pushBack` returns index | Same | O(1) amortized |
| `findIf` stops early | Same | ✅ |
| `remoteExec` uses network | Uses scheduler spawn | Different model |

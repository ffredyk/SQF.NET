// ============================================================
// optimisation.sqf — Performance patterns & best practices
// SQ# Language Sample
// ============================================================

// ============================================================
// Rule 1: Make it work.
// Rule 2: Make it readable.
// Rule 3: Optimise THEN — only after it works and is readable.
// ============================================================

// ---- String Building ----
// SLOW — each + creates a new string (1000 allocations):
// private _msg = "";
// for "_i" from 0 to 999 do {
//     _msg = _msg + "x";
// };

// FAST — build array, join once (1 allocation):
private _parts = [];
for "_i" from 0 to 999 do {
    _parts pushBack "x";
};
private _fastStr = _parts joinString "";

// EVEN FASTER — use format/interpolation for known patterns:
private _n = 42;
private _msg = f"Value is {_n}";  // compiled to single format call

// ---- Array Building ----
// FAST — mutate in place:
private _arr = [];
_arr pushBack _item;              // O(1) amortized
_arr append _otherArray;          // O(n) bulk

// SLOW — creates new array every time:
// _arr = _arr + [_item];         // O(n) — copy entire array!
// _arr = _arr + _otherArray;     // O(n+m) — two copies!

// ---- Loop Optimization ----
private _data = [/* ... large array ... */];
private _len = count _data;

// FAST — for-from-to (compiled as native loop):
for "_i" from 0 to (_len - 1) do {
    private _x = _data select _i;
    processFast(_x);
};

// FAST — forEach with _x:
{ processFast(_x) } forEach _data;

// SLOW — while evaluating count every iteration:
private _i = 0;
// while { _i < count _data } do {  // count called every loop!
//     private _x = _data select _i;
//     processFast(_x);
//     _i = _i + 1;
// };

// FAST — cache the count:
_i = 0;
private _count = count _data;
while { _i < _count } do {
    private _x = _data select _i;
    processFast(_x);
    _i = _i + 1;
};

// ---- Early Exit ----
// SLOW — count always checks ALL elements:
// private _hasTarget = { _x == _target } count _data > 0;

// FAST — findIf stops at first match:
private _hasTarget = (_data findIf { _x == _target }) != -1;

// ---- Filter vs Loop ----
// SLOW — two passes (filter then iterate):
// {
//     processSlow(_x);
// } forEach (_data select { _x > 0 });

// FAST — one pass:
{
    if (_x > 0) then {
        processSlow(_x);
    };
} forEach _data;

// ---- Pre-allocate Large Arrays ----
// When building very large arrays, pre-size if known:
private _size = 10000;
private _big = [];
_big resize _size;                   // pre-allocate
for "_i" from 0 to (_size - 1) do {
    _big set [_i, computeValue(_i)];  // set avoids realloc
};

// ---- Cache Expensive Computations ----
// SLOW:
// for "_i" from 0 to 999 do {
//     private _result = expensiveFunction(someConst);  // same every time!
//     useResult(_result);
// };

// FAST:
private _cachedResult = expensiveFunction(someConst);
for "_i" from 0 to 999 do {
    useResult(_cachedResult);
};

// ---- Use HashMap for lookups not array scan ----
// SLOW — O(n) scan every time:
private _items = [/* 1000 items */];
// private _found = _items findIf { _x == _target };

// FAST — O(1) HashMap lookup:
private _index = createHashMap;
{
    _index set [_x, _forEachIndex];
} forEach _items;
private _foundIdx = _index get _target;  // O(1)!

// ---- Avoid Nested Loops When Possible ----
// SLOW — O(n*m):
// {
//     private _outer = _x;
//     {
//         if (_outer == _x) then { ... };
//     } forEach _listB;
// } forEach _listA;

// FAST — mark visited in HashMap, single pass each:
private _setB = createHashMap;
{ _setB set [_x, true]; } forEach _listB;
{
    if (_x in _setB) then { /* match! */ };
} forEach _listA;

// ---- Use FrozenArray for shared read-only data ----
// When spawning tasks on other schedulers, freeze data first:
private _sharedConfig = [/* large config */] freeze;
// _sharedConfig spawnOn ["AI", { params ["_cfg"]; ... read _cfg ... }];
// FrozenArray is immutable — zero-copy reads from any thread.

// ---- Use apply/select instead of manual loops for clarity ----
// Readable AND often optimized by the compiler:
private _doubled = _data apply { _x * 2 };
private _filtered = _data select { _x > 0 };
private _foundIdx = _data findIf { _x == _target };

// ---- Avoid Repeated Property Access ----
// SLOW:
// for "_i" from 0 to (count _arr - 1) do {
//     private _obj = _arr select _i;
//     if (_obj get "hp" > 0) then {
//         _obj get "hp";           // accessing twice
//     };
// };

// FAST:
for "_i" from 0 to (count _arr - 1) do {
    private _obj = _arr select _i;
    private _hp = _obj get "hp";   // access once
    if (_hp > 0) then {
        processHp(_hp);
    };
};

// ---- Use sleep(0) to yield cooperatively ----
// Long loop in scheduled fiber? Yield occasionally:
private _hugeArray = [/* 100000 items */];
{
    heavyProcessing(_x);
    if (_forEachIndex % 100 == 0) then {
        sleep 0;  // yield to other fibers
    };
} forEach _hugeArray;

// ---- Scheduler-aware optimization ----
// CPU-heavy work → spawnParallel (true parallelism):
private _cpuResult = await (spawnParallel {
    private _sum = 0;
    for "_i" from 1 to 1000000 do {
        _sum = _sum + _i;
    };
    _sum
});

// I/O or host-call-heavy work → own scheduler:
private _ioResult = await (["data.txt"] spawnOn ["IO", {
    params ["_file"];
    // ... file operations ...
    return "done";
}]);

"optimisation complete"

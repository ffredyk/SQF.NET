// ============================================================
// functions.sqf — Code blocks, call, spawn, params, modules
// SQ# Language Sample
// ============================================================

// ---- Code Blocks Are First-Class Values ----
// A code block is a value like any other:
private _doubler = { _this * 2 };

// ---- call — Execute code, inherits caller's environment ----
private _result = 21 call _doubler;        // 42 (unary: operator _right)
// _this = 21 inside the code block

// call with array argument:
private _sum = [10, 20] call { params ["_a", "_b"]; _a + _b; };  // 30

// ---- spawn — Execute as new scheduled fiber ----
// spawn ALWAYS creates a new scheduled script.
// Returns ScriptHandle (promise).
// NO access to parent locals — must pass params explicitly.

private _handle = spawn {
    private _work = "doing heavy work...";
    sleep 2;
    return "done";
};
// _handle is ScriptHandle — can await, check scriptDone, etc.

// spawn with arguments:
[100, "target1"] spawn {
    params ["_value", "_name"];
    print f"Processing {_name} with value {_value}";
    sleep 1;
    print f"{_name} complete";
};

// ---- execVM — Load+compile+spawn a file ----
// private _vmHandle = execVM "script.sqf";
// Equivalent to: spawn compile preprocessFileLineNumbers "script.sqf"

// ---- compile — String → Code (runtime compilation) ----
private _dynamicCode = compile "private _x = 10; _x * 3";
private _dynamicResult = call _dynamicCode;  // 30

// ---- compileScript — File → Code (supports .sqfc bytecode) ----
// private _compiled = compileScript ["script.sqf", false, ""];

// ---- loadFile — Raw file read (no preprocessing) ----
// private _raw = loadFile "data.txt";

// ---- preprocessFileLineNumbers — File → Preprocessed string ----
// private _ppSource = preprocessFileLineNumbers "script.sqf";

// ---- params — Structured argument unpacking ----
// Basic unpack:
private _addThree = {
    params ["_a", "_b", "_c"];
    _a + _b + _c
};
[1, 2, 3] call _addThree;  // 6

// With defaults and type validation:
private _createPlayer = {
    params [
        "_name",                              // required
        ["_age", 0, [0]],                     // default 0, must be Number
        ["_tags", [], [[]], [2, 4]],          // default [], must be Array, 2-4 elements
        ["_optional", nil]                    // default nil, any type
    ];

    print f"Created player: {_name}, age {_age}";
    // Returns false if ANY default was used
    [_name, _age, _tags]
};

// params returns false if defaults used:
["Bob", 25] call _createPlayer;          // OK, tags defaults to []

// Skip elements:
private _getThird = {
    params ["", "", "_onlyThird"];
    _onlyThird
};
[1, 2, 42] call _getThird;               // 42

// Non-array argument auto-wraps:
42 call { params ["_x"]; _x * 2; };       // 84

// ---- param — Single safe access with default ----
private _val = [1, 2, 3] param [5, "fallback"];  // "fallback" (OOB → default)

// ---- Function factories (closures) ----
private _makeMultiplier = {
    params ["_factor"];
    { _this * _factor }     // returns a code block capturing _factor
};

private _triple = 3 call _makeMultiplier;
private _nine = 9 call _triple;           // 27

// ---- Recursive functions ----
private _factorial = {
    params ["_n"];
    if (_n <= 1) exitWith { 1 };
    _n * (_n - 1) call _factorial
};
private _fact5 = 5 call _factorial;       // 120

// ---- Higher-order functions ----
// apply with custom function:
private _addOne = { _this + 1 };
private _nums = [1, 2, 3] apply _addOne;  // [2, 3, 4]

// ---- Compose functions ----
private _compose = {
    params ["_f", "_g"];
    { (_this call _g) call _f }
};

// ---- Named code (global functions) ----
global fn_double = { _this * 2 };
global fn_triple = { _this * 3 };

private _four = 2 call fn_double;         // 4
private _six = 2 call fn_triple;          // 6

// ---- Function with default params pattern ----
global fn_greet = {
    params [
        ["_name", "Stranger", [""]],
        ["_times", 1, [0]]
    ];
    for "_i" from 1 to _times do {
        print f"Hello, {_name}!";
    };
};

"Alice" call fn_greet;                    // "Hello, Alice!" once
["Bob", 3] call fn_greet;                 // "Hello, Bob!" three times

// ---- Private scope — block-level privacy ----
// Variables declared with private inside a code block
// are NOT visible outside:
{
    private _secret = "hidden";
    print _secret;                        // OK — inside block
} call {};
// print _secret;                         // ERROR — _secret not in scope

// ---- toString — Code → String ----
private _codeStr = toString _doubler;     // "{ _this * 2 }"

"functions complete"

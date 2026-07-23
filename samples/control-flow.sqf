// ============================================================
// control-flow.sqf — if/else, while, for, switch, forEach
// SQ# Language Sample
// ============================================================

// ---- if-then-else ----
// Sugar form (clean SQ# style):
private _hp = 75;
if (_hp > 50) {
    print "Healthy";
} else {
    print "Wounded";
};

// Raw operator form also valid:
if (_hp > 0) then { print "Alive"; } else { print "Dead"; };

// Chained conditions:
private _score = 85;
if (_score >= 90) {
    print "A";
} else {
    if (_score >= 80) {
        print "B";
    } else {
        if (_score >= 70) {
            print "C";
        } else {
            print "F";
        };
    };
};

// Short-circuit: conditions evaluate lazily
if (alive player && { damage player < 0.5 }) {
    print "Player healthy enough";
};

// ---- while-do ----
private _i = 0;
while { _i < 5 } do {
    print f"Tick {_i}";
    _i = _i + 1;
};

// while with cached count (optimisation):
private _arr = [10, 20, 30, 40, 50];
_i = 0;
private _len = count _arr;
while { _i < _len } do {
    private _val = _arr select _i;
    print f"Element {_i} = {_val}";
    _i = _i + 1;
};

// ---- for (from-to-step) ----
// Ascending:
for "_j" from 0 to 4 do {
    print f"For loop: {_j}";
};

// Descending with step:
for "_k" from 10 to 0 step -2 do {
    print f"Countdown: {_k}";
};

// ---- for (array form) ----
for [{private _n = 0}, {_n < 3}, {_n = _n + 1}] do {
    print f"Array-for: {_n}";
};

// ---- forEach ----
// Iterate array:
{
    print f"forEach: {_x}";       // _x = current element
} forEach [1, 2, 3, 4, 5];

// With index:
{
    print f"[{_forEachIndex}] = {_x}";
} forEach ["alpha", "bravo", "charlie"];

// ---- switch-do-case-default ----
private _color = "red";
switch (_color) do {
    case "red": {
        print "Warm color";
    };
    case "blue": {
        print "Cool color";
    };
    case "green": {
        print "Nature color";
    };
    default {
        print "Unknown color";
    };
};

// ---- exitWith (early return from scope) ----
private _result = {
    if (_x < 0) exitWith { "negative" };
    if (_x == 0) exitWith { "zero" };
    "positive"
} forEach [5];  // not typical — exitWith used inside code blocks

// exitWith in a validation function:
private _validate = {
    params ["_name", "_age"];
    if (_name == "") exitWith { false };
    if (_age < 0) exitWith { false };
    if (_age > 150) exitWith { false };
    true
};
private _valid = [""] call _validate;  // false

// ---- break / continue (via scope) ----
// Use exitWith to break from loops:
private _found = -1;
{
    if (_x == 42) exitWith { _found = _forEachIndex; };
} forEach [10, 20, 42, 30];
// _found = 2

// ---- Logical short-circuit with code ----
// Use code blocks for expensive lazy evaluation:
private _expensive = { sleep 1; heavyComputation(); };
private _quickCheck = true;
if (_quickCheck || { call _expensive }) {
    print "Short-circuited — expensive never ran";
};

"control-flow complete"

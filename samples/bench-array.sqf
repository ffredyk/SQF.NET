// ============================================================
// bench-array.sqf -- Array operation benchmark
// Measures: pushBack, select, forEach, sort, find, deleteAt.
// Compatible with both SQ# and Arma 3 SQF.
// ============================================================

// ---- Configuration ----
SIZE = 5000;
TRIALS = 5;

bench_arrayPushBack = {
    private _arr = [];
    private _i = 0;
    private _start = diag_tickTime;
    while { _i < SIZE } do {
        _arr pushBack _i;
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

bench_arraySelect = {
    private _arr = [];
    private _i = 0;
    while { _i < SIZE } do { _arr pushBack _i; _i = _i + 1; };

    private _start = diag_tickTime;
    _i = 0;
    while { _i < SIZE } do {
        private _x = _arr select _i;
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

bench_arrayForEach = {
    private _arr = [];
    private _i = 0;
    while { _i < SIZE } do { _arr pushBack _i; _i = _i + 1; };

    private _start = diag_tickTime;
    { private _x = _x } forEach _arr;
    (diag_tickTime - _start) * 1000
};

bench_arraySort = {
    private _arr = [];
    private _i = 0;
    while { _i < SIZE } do { _arr pushBack (random 10000); _i = _i + 1; };

    private _start = diag_tickTime;
    _arr sort true;
    (diag_tickTime - _start) * 1000
};

bench_arrayFind = {
    private _arr = [];
    private _i = 0;
    while { _i < SIZE } do { _arr pushBack _i; _i = _i + 1; };

    private _start = diag_tickTime;
    private _idx = _arr find (SIZE - 1);
    (diag_tickTime - _start) * 1000
};

bench_arrayDeleteAt = {
    private _arr = [];
    private _i = 0;
    while { _i < 500 } do { _arr pushBack _i; _i = _i + 1; };

    private _start = diag_tickTime;
    while { count _arr > 0 } do {
        _arr deleteAt 0;
    };
    (diag_tickTime - _start) * 1000
};

// ---- Runner ----
BENCH_USE_SPAWN = true; // true=Arma3 compat (spawn), false=SQ# (call)
bench_runArray = {
    params ["_name", "_code"];

    private _best = 1e9;
    private _trial = 0;
    while { _trial < TRIALS } do {
        private _t = 0;
        if (BENCH_USE_SPAWN) then {
            private _handle = spawn _code;
            waitUntil { scriptDone _handle };
        } else {
            _t = call _code;
        };
        if (_t < _best) then { _best = _t; };
        _trial = _trial + 1;
    };

    diag_log format ["[BENCH] %1 | best: %2 ms | size: %3", _name, _best, SIZE];
    _best
};

// ---- Run ----
private _t1 = ["pushBack", bench_arrayPushBack] call bench_runArray;
private _t2 = ["select", bench_arraySelect] call bench_runArray;
private _t3 = ["forEach", bench_arrayForEach] call bench_runArray;
private _t4 = ["sort", bench_arraySort] call bench_runArray;
private _t5 = ["find-last", bench_arrayFind] call bench_runArray;
private _t6 = ["deleteAt-front", bench_arrayDeleteAt] call bench_runArray;

systemChat format ["array bench done -- pushBack:%1 select:%2 forEach:%3 sort:%4 find:%5 ms", _t1, _t2, _t3, _t4, _t5];
diag_log format ["[BENCH] array summary | pushBack:%1 select:%2 forEach:%3 sort:%4 find:%5 deleteAt:%6 ms (size=%7)", _t1, _t2, _t3, _t4, _t5, _t6, SIZE];

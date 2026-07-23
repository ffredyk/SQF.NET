// ============================================================
// bench-loop.sqf -- Loop overhead benchmark
// Compares: for-from-to, while-do, forEach (empty body vs math).
// Compatible with both SQ# and Arma 3 SQF.
// ============================================================

// ---- Configuration ----
N = 50000;
TRIALS = 5;

bench_forEmpty = {
    private _start = diag_tickTime;
    for "_i" from 0 to (N - 1) do {};
    (diag_tickTime - _start) * 1000
};

bench_whileEmpty = {
    private _start = diag_tickTime;
    private _i = 0;
    while { _i < N } do { _i = _i + 1; };
    (diag_tickTime - _start) * 1000
};

bench_forEachEmpty = {
    private _arr = [];
    private _i = 0;
    while { _i < N } do { _arr pushBack 0; _i = _i + 1; };

    private _start = diag_tickTime;
    {} forEach _arr;
    (diag_tickTime - _start) * 1000
};

bench_forMath = {
    private _start = diag_tickTime;
    private _acc = 0;
    for "_i" from 0 to (N - 1) do {
        _acc = _acc + sin(_i) * cos(_i);
    };
    (diag_tickTime - _start) * 1000
};

bench_whileMath = {
    private _start = diag_tickTime;
    private _acc = 0;
    private _i = 0;
    while { _i < N } do {
        _acc = _acc + sin(_i) * cos(_i);
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

bench_forEachMath = {
    private _arr = [];
    private _i = 0;
    while { _i < N } do { _arr pushBack _i; _i = _i + 1; };

    private _start = diag_tickTime;
    private _acc = 0;
    { _acc = _acc + sin(_x) * cos(_x) } forEach _arr;
    (diag_tickTime - _start) * 1000
};

bench_forStep = {
    private _start = diag_tickTime;
    private _acc = 0;
    for "_i" from 0 to (N - 1) step 1 do {
        _acc = _acc + _i;
    };
    (diag_tickTime - _start) * 1000
};

// ---- Runner ----
BENCH_USE_SPAWN = true; // true=Arma3 compat (spawn), false=SQ# (call)
bench_runLoop = {
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
    diag_log format ["[BENCH] %1 | best: %2 ms | loops: %3", _name, _best, N];
    _best
};

// ---- Run ----
private _t1 = ["for-empty", bench_forEmpty] call bench_runLoop;
private _t2 = ["while-empty", bench_whileEmpty] call bench_runLoop;
private _t3 = ["forEach-empty", bench_forEachEmpty] call bench_runLoop;
private _t4 = ["for-math", bench_forMath] call bench_runLoop;
private _t5 = ["while-math", bench_whileMath] call bench_runLoop;
private _t6 = ["forEach-math", bench_forEachMath] call bench_runLoop;
private _t7 = ["for-step", bench_forStep] call bench_runLoop;

systemChat format ["loop bench done -- for:%1 while:%2 forEach:%3 ms", _t1, _t2, _t3];
diag_log format ["[BENCH] loop summary | for-e:%1 while-e:%2 forEach-e:%3 for-m:%4 while-m:%5 forEach-m:%6 step:%7 ms (N=%8)", _t1, _t2, _t3, _t4, _t5, _t6, _t7, N];

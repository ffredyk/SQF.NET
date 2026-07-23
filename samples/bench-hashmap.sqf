// ============================================================
// bench-hashmap.sqf -- HashMap operation benchmark
// Measures: createHashMap, set, get, createHashMapFromArray.
// Compatible with both SQ# and Arma 3 SQF (HashMap since 2.02).
// ============================================================

// ---- Configuration ----
SIZE = 2000;
TRIALS = 5;

bench_hashSet = {
    private _start = diag_tickTime;
    private _map = createHashMap;
    private _i = 0;
    while { _i < SIZE } do {
        _map set [format ["key_%1", _i], _i];
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

bench_hashGet = {
    private _map = createHashMap;
    private _i = 0;
    while { _i < SIZE } do { _map set [format ["key_%1", _i], _i]; _i = _i + 1; };

    private _start = diag_tickTime;
    _i = 0;
    while { _i < SIZE } do {
        private _v = _map get (format ["key_%1", _i]);
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

bench_hashCreateFromArray = {
    private _flat = [];
    private _i = 0;
    while { _i < SIZE } do {
        _flat pushBack (format ["k%1", _i]);
        _flat pushBack _i;
        _i = _i + 1;
    };

    private _start = diag_tickTime;
    private _map = createHashMapFromArray _flat;
    (diag_tickTime - _start) * 1000
};

bench_hashMixed = {
    private _map = createHashMap;
    private _start = diag_tickTime;
    private _i = 0;
    while { _i < SIZE } do {
        _map set [format ["k%1", _i], _i];
        private _v = _map get (format ["k%1", _i mod 100]);
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

// ---- Runner ----
BENCH_USE_SPAWN = true; // true=Arma3 compat (spawn), false=SQ# (call)
bench_runHash = {
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
private _t1 = ["set", bench_hashSet] call bench_runHash;
private _t2 = ["get", bench_hashGet] call bench_runHash;
private _t3 = ["createFromArray", bench_hashCreateFromArray] call bench_runHash;
private _t4 = ["set+get", bench_hashMixed] call bench_runHash;

systemChat format ["hashmap bench done -- set:%1 get:%2 fromArray:%3 ms", _t1, _t2, _t3];
diag_log format ["[BENCH] hashmap summary | set:%1 get:%2 fromArray:%3 mixed:%4 ms (size=%5)", _t1, _t2, _t3, _t4, SIZE];

// ============================================================
// bench-string.sqf -- String operation benchmark
// Measures: concatenation, format, splitString, joinString, find.
// Compatible with both SQ# and Arma 3 SQF.
// ============================================================

// ---- Configuration ----
COUNT = 2000;
TRIALS = 5;

bench_stringConcat = {
    private _start = diag_tickTime;
    private _s = "";
    private _i = 0;
    while { _i < COUNT } do {
        _s = _s + "x";
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

bench_stringJoinFast = {
    private _start = diag_tickTime;
    private _parts = [];
    private _i = 0;
    while { _i < COUNT } do {
        _parts pushBack "x";
        _i = _i + 1;
    };
    private _s = _parts joinString "";
    (diag_tickTime - _start) * 1000
};

bench_format = {
    private _start = diag_tickTime;
    private _i = 0;
    while { _i < COUNT } do {
        private _s = format ["HP: %1/%2 Mana: %3/%4", _i, _i + 100, _i * 2, _i * 3];
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

bench_splitJoin = {
    private _base = "";
    private _i = 0;
    while { _i < 200 } do { _base = _base + "word,"; _i = _i + 1; };

    private _start = diag_tickTime;
    _i = 0;
    while { _i < COUNT } do {
        private _parts = _base splitString ",";
        private _joined = _parts joinString "-";
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

bench_stringFind = {
    private _base = "abcdefghijklmnopqrstuvwxyz";
    private _needle = "xyz";

    private _start = diag_tickTime;
    private _i = 0;
    while { _i < COUNT } do {
        private _idx = _base find _needle;
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

bench_toUpperLower = {
    private _base = "The Quick Brown Fox Jumps Over The Lazy Dog";
    private _start = diag_tickTime;
    private _i = 0;
    while { _i < COUNT } do {
        private _u = toUpper _base;
        private _l = toLower _u;
        _i = _i + 1;
    };
    (diag_tickTime - _start) * 1000
};

// ---- Runner ----
bench_runStr = {
    params ["_name", "_code"];
    private _best = 1e9;
    private _trial = 0;
    while { _trial < TRIALS } do {
        private _t = call _code;
        if (_t < _best) then { _best = _t; };
        _trial = _trial + 1;
    };
    diag_log format ["[BENCH] %1 | best: %2 ms | ops: %3", _name, _best, COUNT];
    _best
};

// ---- Run ----
private _t1 = ["concat-naive", bench_stringConcat] call bench_runStr;
private _t2 = ["join-fast", bench_stringJoinFast] call bench_runStr;
private _t3 = ["format", bench_format] call bench_runStr;
private _t4 = ["split+join", bench_splitJoin] call bench_runStr;
private _t5 = ["find", bench_stringFind] call bench_runStr;
private _t6 = ["toUpper+toLower", bench_toUpperLower] call bench_runStr;

systemChat format ["string bench done -- concat:%1 join:%2 format:%3 ms", _t1, _t2, _t3];
diag_log format ["[BENCH] string summary | concat:%1 join:%2 format:%3 split+join:%4 find:%5 case:%6 ms (ops=%7)", _t1, _t2, _t3, _t4, _t5, _t6, COUNT];

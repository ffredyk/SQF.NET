// ============================================================
// init.sqf — SQ# / Arma 3 Performance Benchmark Suite
// Drop into mission root, runs on mission start.
// All benchmarks run sequentially. Results via systemChat + diag_log.
// ============================================================

if (!isNil "BENCH_RUN") exitWith {};  // prevent double-run
BENCH_RUN = true;

// ---- Config ----
BENCH_TRIALS = 3;       // best-of-N trials per test
BENCH_MATH_OPS = 50000; // math loop iterations
BENCH_ARRAY_SIZE = 2000;// array element count
BENCH_STRING_OPS = 1000;// string operation count
BENCH_HASH_SIZE = 1000; // hashmap key count
BENCH_LOOP_N = 30000;   // loop iteration count

// ---- Runner ----
bench_run = {
    params ["_name", "_code"];

    private _best = 1e9;
    private _results = [];
    private _trial = 0;

    while { _trial < BENCH_TRIALS } do {
        private _start = diag_tickTime;
        call _code;
        private _elapsed = (diag_tickTime - _start) * 1000;
        _results pushBack _elapsed;
        if (_elapsed < _best) then { _best = _elapsed; };
        _trial = _trial + 1;
    };

    diag_log format ["[BENCH] %1 | best: %2 ms | trials: %3", _name, _best, _results];
    systemChat format ["%1: %2 ms", _name, (floor (_best * 100)) / 100];
    _best
};

// ============================================================
// 1. MATH — Trig + sqrt + log + pow
// ============================================================
bench_math = {
    private _acc = 0.0;
    private _i = 0;
    while { _i < BENCH_MATH_OPS } do {
        _acc = _acc + sin(_i) + cos(_i) + tan(_i mod 360);
        _acc = _acc + sqrt(abs _i) + log(abs _i + 1);
        _acc = _acc + (_i ^ 0.5) + (2 ^ (_i mod 10));
        _i = _i + 1;
    };
    _acc
};

// ============================================================
// 2. ARRAY — pushBack / select / forEach / sort / find / deleteAt
// ============================================================
bench_arrayPushBack = {
    private _arr = [];
    private _i = 0;
    while { _i < BENCH_ARRAY_SIZE } do { _arr pushBack _i; _i = _i + 1; };
};

bench_arraySelect = {
    private _arr = [];
    private _i = 0;
    while { _i < BENCH_ARRAY_SIZE } do { _arr pushBack _i; _i = _i + 1; };
    _i = 0;
    while { _i < BENCH_ARRAY_SIZE } do { private _x = _arr select _i; _i = _i + 1; };
};

bench_arrayForEach = {
    private _arr = [];
    private _i = 0;
    while { _i < BENCH_ARRAY_SIZE } do { _arr pushBack _i; _i = _i + 1; };
    { private _x = _x } forEach _arr;
};

bench_arraySort = {
    private _arr = [];
    private _i = 0;
    while { _i < BENCH_ARRAY_SIZE } do { _arr pushBack (random 10000); _i = _i + 1; };
    _arr sort true;
};

bench_arrayFind = {
    private _arr = [];
    private _i = 0;
    while { _i < BENCH_ARRAY_SIZE } do { _arr pushBack _i; _i = _i + 1; };
    private _idx = _arr find (BENCH_ARRAY_SIZE - 1);
};

// ============================================================
// 3. STRING — concat / joinString / format / splitString / find
// ============================================================
bench_stringConcat = {
    private _s = "";
    private _i = 0;
    while { _i < BENCH_STRING_OPS } do { _s = _s + "x"; _i = _i + 1; };
};

bench_stringJoin = {
    private _parts = [];
    private _i = 0;
    while { _i < BENCH_STRING_OPS } do { _parts pushBack "x"; _i = _i + 1; };
    private _s = _parts joinString "";
};

bench_format = {
    private _i = 0;
    while { _i < BENCH_STRING_OPS } do {
        private _s = format ["HP: %1/%2 Mana: %3/%4", _i, _i + 100, _i * 2, _i * 3];
        _i = _i + 1;
    };
};

bench_splitJoin = {
    private _base = "";
    private _i = 0;
    while { _i < 100 } do { _base = _base + "word,"; _i = _i + 1; };
    _i = 0;
    while { _i < BENCH_STRING_OPS } do {
        private _parts = _base splitString ",";
        private _joined = _parts joinString "-";
        _i = _i + 1;
    };
};

bench_stringFind = {
    private _base = "abcdefghijklmnopqrstuvwxyz";
    private _i = 0;
    while { _i < BENCH_STRING_OPS } do { private _idx = _base find "xyz"; _i = _i + 1; };
};

bench_toUpperLower = {
    private _base = "The Quick Brown Fox Jumps Over The Lazy Dog";
    private _i = 0;
    while { _i < BENCH_STRING_OPS } do {
        private _u = toUpper _base;
        private _l = toLower _u;
        _i = _i + 1;
    };
};

// ============================================================
// 4. HASHMAP — set / get / createHashMapFromArray
// ============================================================
bench_hashSet = {
    private _map = createHashMap;
    private _i = 0;
    while { _i < BENCH_HASH_SIZE } do {
        _map set [format ["key_%1", _i], _i];
        _i = _i + 1;
    };
};

bench_hashGet = {
    private _map = createHashMap;
    private _i = 0;
    while { _i < BENCH_HASH_SIZE } do { _map set [format ["key_%1", _i], _i]; _i = _i + 1; };
    _i = 0;
    while { _i < BENCH_HASH_SIZE } do {
        private _v = _map get (format ["key_%1", _i]);
        _i = _i + 1;
    };
};

bench_hashMixed = {
    private _map = createHashMap;
    private _i = 0;
    while { _i < BENCH_HASH_SIZE } do {
        _map set [format ["k%1", _i], _i];
        private _v = _map get (format ["k%1", _i mod 100]);
        _i = _i + 1;
    };
};

// ============================================================
// 5. LOOPS — for / while / forEach (empty + math body)
// ============================================================
bench_forEmpty = {
    for "_i" from 0 to (BENCH_LOOP_N - 1) do {};
};

bench_whileEmpty = {
    private _i = 0;
    while { _i < BENCH_LOOP_N } do { _i = _i + 1; };
};

bench_forEachEmpty = {
    private _arr = [];
    private _i = 0;
    while { _i < BENCH_LOOP_N } do { _arr pushBack 0; _i = _i + 1; };
    {} forEach _arr;
};

bench_forMath = {
    private _acc = 0;
    for "_i" from 0 to (BENCH_LOOP_N - 1) do {
        _acc = _acc + sin(_i) * cos(_i);
    };
};

bench_whileMath = {
    private _acc = 0;
    private _i = 0;
    while { _i < BENCH_LOOP_N } do {
        _acc = _acc + sin(_i) * cos(_i);
        _i = _i + 1;
    };
};

bench_forEachMath = {
    private _arr = [];
    private _i = 0;
    while { _i < BENCH_LOOP_N } do { _arr pushBack _i; _i = _i + 1; };
    private _acc = 0;
    { _acc = _acc + sin(_x) * cos(_x) } forEach _arr;
};

// ============================================================
// RUN ALL
// ============================================================
systemChat "--- SQ# Benchmarks ---";
diag_log "========== SQ# BENCHMARKS START ==========";

// Math
private _m1 = ["math-trig", bench_math] call bench_run;

// Array
private _a1 = ["arr-pushBack", bench_arrayPushBack] call bench_run;
private _a2 = ["arr-select", bench_arraySelect] call bench_run;
private _a3 = ["arr-forEach", bench_arrayForEach] call bench_run;
private _a4 = ["arr-sort", bench_arraySort] call bench_run;
private _a5 = ["arr-find", bench_arrayFind] call bench_run;

// String
private _s1 = ["str-concat", bench_stringConcat] call bench_run;
private _s2 = ["str-join", bench_stringJoin] call bench_run;
private _s3 = ["str-format", bench_format] call bench_run;
private _s4 = ["str-splitJoin", bench_splitJoin] call bench_run;
private _s5 = ["str-find", bench_stringFind] call bench_run;
private _s6 = ["str-case", bench_toUpperLower] call bench_run;

// Hashmap
private _h1 = ["hash-set", bench_hashSet] call bench_run;
private _h2 = ["hash-get", bench_hashGet] call bench_run;
private _h3 = ["hash-mixed", bench_hashMixed] call bench_run;

// Loops
private _l1 = ["for-empty", bench_forEmpty] call bench_run;
private _l2 = ["while-empty", bench_whileEmpty] call bench_run;
private _l3 = ["forEach-empty", bench_forEachEmpty] call bench_run;
private _l4 = ["for-math", bench_forMath] call bench_run;
private _l5 = ["while-math", bench_whileMath] call bench_run;
private _l6 = ["forEach-math", bench_forEachMath] call bench_run;

// Summary
diag_log "========== SQ# BENCHMARKS END ==========";

// Full results table — copy this into benchmark doc
diag_log format ["RESULT|math-trig|%1|ms", _m1];
diag_log format ["RESULT|arr-pushBack|%1|ms", _a1];
diag_log format ["RESULT|arr-select|%1|ms", _a2];
diag_log format ["RESULT|arr-forEach|%1|ms", _a3];
diag_log format ["RESULT|arr-sort|%1|ms", _a4];
diag_log format ["RESULT|arr-find|%1|ms", _a5];
diag_log format ["RESULT|str-concat|%1|ms", _s1];
diag_log format ["RESULT|str-join|%1|ms", _s2];
diag_log format ["RESULT|str-format|%1|ms", _s3];
diag_log format ["RESULT|str-splitJoin|%1|ms", _s4];
diag_log format ["RESULT|str-find|%1|ms", _s5];
diag_log format ["RESULT|str-case|%1|ms", _s6];
diag_log format ["RESULT|hash-set|%1|ms", _h1];
diag_log format ["RESULT|hash-get|%1|ms", _h2];
diag_log format ["RESULT|hash-mixed|%1|ms", _h3];
diag_log format ["RESULT|for-empty|%1|ms", _l1];
diag_log format ["RESULT|while-empty|%1|ms", _l2];
diag_log format ["RESULT|forEach-empty|%1|ms", _l3];
diag_log format ["RESULT|for-math|%1|ms", _l4];
diag_log format ["RESULT|while-math|%1|ms", _l5];
diag_log format ["RESULT|forEach-math|%1|ms", _l6];

systemChat "--- Benchmarks Complete ---";
systemChat format ["Math:%1 Arr:%2/%3/%4 Str:%5/%6 Loop:%7/%8",
    _m1, _a1, _a2, _a3, _s1, _s2, _l1, _l2];

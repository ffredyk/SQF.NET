// ============================================================
// bench-math.sqf -- Math-heavy CPU benchmark
// Measures raw math throughput: trig, sqrt, log, exp, pow.
// Compatible with both SQ# and Arma 3 SQF.
// ============================================================

// ---- Configuration ----
ITERATIONS = 100000;
TRIALS = 5;

// ---- Math workload ----
bench_mathWorkload = {
    private _acc = 0.0;
    private _i = 0;
    while { _i < ITERATIONS } do {
        _acc = _acc + sin(_i) + cos(_i) + tan(_i mod 360);
        _acc = _acc + sqrt(abs _i) + log(abs _i + 1);
        _acc = _acc + (_i ^ 0.5) + (2 ^ (_i mod 10));
        _i = _i + 1;
    };
    _acc
};

// ---- Runner ----
BENCH_USE_SPAWN = true; // true=Arma3 compat (spawn), false=SQ# (call)
bench_run = {
    params ["_name", "_code"];

    private _best = 1e9;
    private _results = [];

    private _trial = 0;
    while { _trial < TRIALS } do {
        private _start = diag_tickTime;
        if (BENCH_USE_SPAWN) then {
            private _handle = spawn _code;
            waitUntil { scriptDone _handle };
        } else {
            private _dummy = call _code;
        };
        private _elapsed = diag_tickTime - _start;
        _results pushBack _elapsed;
        if (_elapsed < _best) then { _best = _elapsed; };
        _trial = _trial + 1;
    };

    diag_log format ["[BENCH] %1 | best: %2 ms | trials: %3", _name, _best * 1000, _results];
    _best
};

// ---- Run ----
private _t = ["math-trig-heavy", bench_mathWorkload] call bench_run;
diag_log format ["[BENCH] math done -- %1 ms (%2 ops)", _t * 1000, ITERATIONS];
systemChat format ["math bench: %1 ms", _t * 1000];

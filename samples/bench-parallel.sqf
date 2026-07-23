// ============================================================
// bench-parallel.sqf — Parallelism Benchmark (SQ# + Arma 3)
// Measures throughput scaling with concurrent fibers.
// Auto-detects engine: await (SQ#) vs waitUntil+scriptDone (Arma).
// Parallel test (spawnOn) is SQ#-only — returns -1 in Arma.
//
// NOTE for Arma 3 users:
//   Arma parser chokes on `await` keyword. Search for "ARMA" comments
//   and follow instructions to adapt the script for Arma.
// ============================================================

// ---- Config ----
PARALLEL_OPS = 40000;
FIBER_COUNT = 8;
BENCH_TRIALS = 3;

// Engine detection — isNil { code } returns true if code throws (undefined command/variable)
SQSHARP = !(isNil { await nil });

// ---- Workload ----
bench_work = {
    private _acc = 0.0;
    private _i = 0;
    while { _i < PARALLEL_OPS } do {
        _acc = _acc + sin(_i) * cos(_i) + sqrt(_i + 1);
        _i = _i + 1;
    };
    _acc
};

// ---- Sequential (single-thread) ----
bench_sequential = {
    private _start = diag_tickTime;
    private _handles = [];
    private _i = 0;

    while { _i < FIBER_COUNT } do {
        private _h = spawn bench_work;
        _handles pushBack _h;
        _i = _i + 1;
    };

    // Wait for all to finish
    private _j = 0;
    while { _j < count _handles } do {
        private _h = _handles select _j;
        if (SQSHARP) then {
            await _h
        } else {
            waitUntil { scriptDone _h }
        };
        _j = _j + 1;
    };

    (diag_tickTime - _start) * 1000
};

// ---- Parallel (SQ# multi-scheduler) ----
bench_parallel = {
    if (!SQSHARP) exitWith { -1 };

    private _schedulers = ["B_1", "B_2", "B_3", "B_4"];
    private _start = diag_tickTime;
    private _handles = [];
    private _i = 0;

    while { _i < FIBER_COUNT } do {
        private _sched = _schedulers select (_i mod (count _schedulers));
        private _h = spawnOn [_sched, bench_work];
        _handles pushBack _h;
        _i = _i + 1;
    };

    private _j = 0;
    while { _j < count _handles } do {
        await (_handles select _j);
        _j = _j + 1;
    };

    (diag_tickTime - _start) * 1000
};

// ---- Runner: runs trial, takes best time ----
// SQ#: uses spawn+await (call doesn't suspend)
// Arma: uses call (call suspends fine in scheduled environment)
if (SQSHARP) then {
    runTrial = {
        params ["_name", "_code"];
        private _best = 1e9;
        private _trial = 0;
        while { _trial < BENCH_TRIALS } do {
            private _h = spawn _code;
            private _t = await _h;
            if (_t > 0 && _t < _best) then { _best = _t; };
            _trial = _trial + 1;
        };
        if (_best >= 1e9) exitWith { -1 };
        diag_log format ["RESULT|%1|%2|ms", _name, _best];
        systemChat format ["%1: %2 ms", _name, (floor (_best * 100)) / 100];
        _best
    };
} else {
    runTrial = {
        params ["_name", "_code"];
        private _best = 1e9;
        private _trial = 0;
        while { _trial < BENCH_TRIALS } do {
            private _t = call _code;
            if (_t > 0 && _t < _best) then { _best = _t; };
            _trial = _trial + 1;
        };
        if (_best >= 1e9) exitWith { -1 };
        diag_log format ["RESULT|%1|%2|ms", _name, _best];
        systemChat format ["%1: %2 ms", _name, (floor (_best * 100)) / 100];
        _best
    };
};

// ============================================================
// RUN
// ============================================================
systemChat "--- Parallel Bench ---";
diag_log "========== PARALLEL BENCH START ==========";
diag_log format ["CONFIG|fibers=%1|ops=%2|trials=%3", FIBER_COUNT, PARALLEL_OPS, BENCH_TRIALS];

// Engine-specific run
if (SQSHARP) then {
    // SQ#: await works at any level
    private _hSeq = ["seq-1thread", bench_sequential] spawn runTrial;
    private _tSeq = await _hSeq;

    private _hPar = ["par-4sched", bench_parallel] spawn runTrial;
    private _tPar = await _hPar;

    private _speedup = if (_tPar > 0 && _tSeq > 0) then { _tSeq / _tPar } else { -1 };

    diag_log format ["RESULT|speedup|%1|x", _speedup];
    diag_log "========== PARALLEL BENCH END ==========";
    systemChat format ["Seq:%1 Par:%2 Speedup:%3x", _tSeq, _tPar, (floor (_speedup * 100)) / 100];
} else {
    // Arma 3: spawn into scheduled environment
    [] spawn {
        private _hSeq = ["seq-1thread", bench_sequential] spawn runTrial;
        private _hPar = ["par-4sched", bench_parallel] spawn runTrial;

        waitUntil { scriptDone _hSeq };
        waitUntil { scriptDone _hPar };

        diag_log "========== PARALLEL BENCH END ==========";
    };
};

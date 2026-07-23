// ============================================================
// bench-parallel.sqf — Parallelism Benchmark (SQ# only)
// Measures throughput scaling with concurrent fibers across schedulers.
// Uses spawnOn + await — SQ# extensions not available in Arma 3 SQF.
// ============================================================

// ---- Config ----
PARALLEL_OPS = 40000;
FIBER_COUNT = 8;
BENCH_TRIALS = 3;

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

// ---- Sequential (single-thread, Arma-equivalent) ----
// Uses spawn to create fibers on current scheduler.
// All fibers share one thread — cooperatively interleaved.
bench_sequential = {
    private _start = diag_tickTime;
    private _handles = [];
    private _i = 0;

    while { _i < FIBER_COUNT } do {
        private _h = spawn bench_work;
        _handles pushBack _h;
        _i = _i + 1;
    };

    // Wait for all to finish (they run cooperatively on one thread)
    private _j = 0;
    while { _j < count _handles } do {
        await (_handles select _j);
        _j = _j + 1;
    };

    (diag_tickTime - _start) * 1000
};

// ---- Parallel (SQ# multi-scheduler) ----
// Distributes fibers across 4 named schedulers, each on its own thread.
bench_parallel = {    private _schedulers = ["B_1", "B_2", "B_3", "B_4"];
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

// ============================================================
// RUN — use spawn+await at top level (call doesn't support suspension)
// ============================================================
systemChat "--- Parallel Bench ---";
diag_log "========== PARALLEL BENCH START ==========";
diag_log format ["CONFIG|fibers=%1|ops=%2|trials=%3", FIBER_COUNT, PARALLEL_OPS, BENCH_TRIALS];

// Sequential benchmark
private _hSeq = spawn bench_sequential;
private _tSeq = await _hSeq;
diag_log format ["RESULT|seq-1thread|%1|ms", _tSeq];
systemChat format ["seq-1thread: %1 ms", (floor (_tSeq * 100)) / 100];

// Parallel benchmark
private _hPar = spawn bench_parallel;
private _tPar = await _hPar;
diag_log format ["RESULT|par-4sched|%1|ms", _tPar];
systemChat format ["par-4sched: %1 ms", (floor (_tPar * 100)) / 100];

private _speedup = if (_tPar > 0 && _tSeq > 0) then { _tSeq / _tPar } else { -1 };

diag_log format ["RESULT|speedup|%1|x", _speedup];
diag_log "========== PARALLEL BENCH END ==========";
systemChat format ["Seq:%1 Par:%2 Speedup:%3x", _tSeq, _tPar, (floor (_speedup * 100)) / 100];

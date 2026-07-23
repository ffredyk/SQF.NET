// ============================================================
// async-promises.sqf — spawn, await, sleep, ScriptHandle (promises)
// SQ# Language Sample
// ============================================================

// ---- sleep — Suspend fiber for N seconds ----
// Only works in scheduled environment (spawn, execVM).
// Check canSuspend before calling sleep from call:
if (canSuspend) then {
    sleep 0.5;                             // Resume after 500ms
};

// ---- spawn returns ScriptHandle (Promise) ----
private _handle1 = spawn {
    sleep 1;
    return "task1 result";
};

// ---- scriptDone — Check if handle resolved ----
private _isDone = scriptDone _handle1;     // false (still sleeping)

// ---- await — Suspend fiber until promise resolves ----
// await is cooperative: suspends fiber, not thread.
// Works ONLY in scheduled fibers.

// Simple await:
private _result1 = await _handle1;         // "task1 result" (blocks fiber)

// await with timeout:
private _handle2 = spawn {
    sleep 5;
    return "slow result";
};
private _result2 = await _handle2 timeout 2;  // nil (timed out after 2s)

// ---- waitUntil — SQF compat form ----
// waitUntil _handle;                    // await _handle
// waitUntil [_handle, 60];              // await _handle timeout 60

// ---- continueWith — Non-blocking callback ----
private _handle3 = spawn {
    sleep 0.5;
    return "callback result";
};
_handle3 continueWith {
    print f"Callback received: {_this}";
};

// ---- terminate — Cancel promise with optional value ----
private _handle4 = spawn {
    sleep 10;
    return "never reached";
};
_handle4 terminate "cancelled early";      // Kills script, resolves with value
private _cancelledResult = await _handle4;  // "cancelled early"

// ---- Promise combinators (SQ# additions) ----

// PromiseAll — wait for all promises:
private _allResults = PromiseAll [
    spawn { sleep 0.3; return "A"; },
    spawn { sleep 0.5; return "B"; },
    spawn { sleep 0.1; return "C"; }
];
// _allResults = ["A", "B", "C"]

// PromiseRace — first to resolve wins, rest cancelled:
private _winner = PromiseRace [
    spawn { sleep 0.5; return "slow"; },
    spawn { sleep 0.1; return "fast"; }
];
// _winner = "fast"

// PromiseAny — first to resolve successfully (ignore errors):
private _first = PromiseAny [
    spawn { sleep 0.2; return "ok"; },
    spawn { sleep 0.1; throw "error"; }
];
// _first = "ok" (error from second promise ignored)

// ---- Chained async operations ----
private _loadAssets = {
    private _textures = await (spawn { sleep 0.5; return ["tex1", "tex2"]; });
    print f"Loaded {count _textures} textures";

    private _models = await (spawn { sleep 0.3; return ["model1"]; });
    print f"Loaded {count _models} models";

    // Return combined result:
    [_textures, _models]
};

private _assets = spawn _loadAssets;
// ... do other work ...
private _allAssets = await _assets;

// ---- progress — Report progress from within script (SQ# addition) ----
private _longTask = spawn {
    for "_i" from 0 to 100 do {
        progress _i;                     // Report progress 0-100
        sleep 0.01;
    };
    return "complete";
};
_longTask onProgress {
    print f"Progress: {_this}%";
};
private _final = await _longTask;        // "complete"

// ---- Continuation on specific scheduler (SQ# addition) ----
// _handle continueWithOn [""Main"", {
//     updateUI(_this);                   // Run callback on Main scheduler
// }];

// ---- Self-reference: _thisScript ----
private _selfHandle = spawn {
    print f"My handle: {_thisScript}";
    private _done = scriptDone _thisScript;  // false
    return "self-done";
};

// ---- isNull — Check if handle is null/resolved (Arma 3 compat) ----
// In Arma 3, completed handles become null.
// In SQ#, use scriptDone for clarity:
private _nullCheck = isNull _handle1;     // false (not null)
// After resolve: isNull becomes true

// ---- Create empty promise (no backing script) ----
private _promise = spawn "DataRequest";
// Resolve it later:
// _promise terminate ["data", 42];
// private _data = await _promise;       // ["data", 42]

"async-promises complete"

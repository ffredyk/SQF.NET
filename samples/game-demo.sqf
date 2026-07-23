// ============================================================
// game-demo.sqf — Complete game-like simulation using SQ#
// Demonstrates: AI scheduling, combat, waves, scoring, promises
// ============================================================

// ---- Game State (globals in missionNamespace) ----
global GAME_SCORE = 0;
global GAME_WAVE = 0;
global GAME_PLAYER_HP = 100;
global GAME_ENEMIES_ALIVE = 0;
global GAME_IS_RUNNING = true;

// ---- Utility: Log message with timestamp ----
global fn_log = {
    params ["_msg"];
    print f"[GAME] {_msg}";
};

// ---- Utility: Clamp number between min and max ----
global fn_clamp = {
    params ["_val", "_min", "_max"];
    (_val max _min) min _max
};

// ---- Player: Take damage ----
global fn_damagePlayer = {
    params ["_amount"];
    GAME_PLAYER_HP = GAME_PLAYER_HP - _amount;
    GAME_PLAYER_HP = [GAME_PLAYER_HP, 0, 100] call fn_clamp;

    f"Player took {_amount} damage. HP: {GAME_PLAYER_HP}" call fn_log;

    if (GAME_PLAYER_HP <= 0) then {
        "!!! PLAYER DIED !!!" call fn_log;
        GAME_IS_RUNNING = false;
    };
};

// ---- Player: Heal ----
global fn_healPlayer = {
    params ["_amount"];
    GAME_PLAYER_HP = GAME_PLAYER_HP + _amount;
    GAME_PLAYER_HP = [GAME_PLAYER_HP, 0, 100] call fn_clamp;
    f"Player healed {_amount}. HP: {GAME_PLAYER_HP}" call fn_log;
};

// ---- Enemy: Simulate AI behavior ----
global fn_enemyAI = {
    params ["_enemyId"];

    f"Enemy {_enemyId}: Spawned" call fn_log;
    sleep (0.5 + random 1.0);             // Patrol delay

    f"Enemy {_enemyId}: Detected player!" call fn_log;
    sleep 0.3;

    // Attack loop:
    for "_attack" from 1 to (2 + floor random 3) do {
        if (!GAME_IS_RUNNING) exitWith {};

        private _dmg = 5 + floor random 15;
        f"Enemy {_enemyId}: Attacks for {_dmg} damage!" call fn_log;
        _dmg call fn_damagePlayer;
        sleep (0.3 + random 0.5);
    };

    GAME_ENEMIES_ALIVE = GAME_ENEMIES_ALIVE - 1;
    f"Enemy {_enemyId}: Defeated. {GAME_ENEMIES_ALIVE} remaining." call fn_log;
    GAME_SCORE = GAME_SCORE + 100;
};

// ---- Wave: Spawn a wave of enemies ----
global fn_spawnWave = {
    params ["_waveNumber"];

    private _enemyCount = 2 + _waveNumber * 2;
    f"=== WAVE {_waveNumber}: Spawning {_enemyCount} enemies ===" call fn_log;

    GAME_WAVE = _waveNumber;
    GAME_ENEMIES_ALIVE = _enemyCount;

    // Spawn all enemies on "AI" scheduler (parallel AI processing!)
    private _handles = [];
    for "_i" from 1 to _enemyCount do {
        private _name = f"Enemy_{_waveNumber}_{_i}";
        private _h = [_name] spawnOn ["AI", fn_enemyAI];
        _handles pushBack _h;
    };

    // Wait for all enemies in this wave to be defeated:
    private _results = await (PromiseAll _handles);
    f"=== WAVE {_waveNumber}: Complete! ===" call fn_log;
    GAME_SCORE = GAME_SCORE + 500 * _waveNumber;  // Wave bonus
};

// ---- Game Loop: Run waves until player dies ----
global fn_gameLoop = {
    "=== GAME STARTED ===" call fn_log;
    f"Player HP: {GAME_PLAYER_HP}" call fn_log;

    private _wave = 1;
    while { GAME_IS_RUNNING && _wave <= 5 } do {
        _wave call fn_spawnWave;

        if (!GAME_IS_RUNNING) exitWith {};

        // Between waves: brief respite + auto-heal
        "--- Intermission: Healing ---" call fn_log;
        20 call fn_healPlayer;
        sleep 1.0;

        _wave = _wave + 1;
    };

    // ---- Game Over ----
    "========== GAME OVER ==========" call fn_log;
    if (GAME_PLAYER_HP > 0) then {
        "*** VICTORY! All waves cleared! ***" call fn_log;
    } else {
        "*** DEFEAT! Player eliminated. ***" call fn_log;
    };
    f"Final Score: {GAME_SCORE}" call fn_log;
    f"Waves Survived: {GAME_WAVE}" call fn_log;
    f"Player HP: {GAME_PLAYER_HP}" call fn_log;

    GAME_SCORE
};

// ---- Launch the game ----
private _gameHandle = spawn fn_gameLoop;

// Optional: watch progress from another fiber
spawn {
    while { GAME_IS_RUNNING } do {
        sleep 2;
        if (GAME_IS_RUNNING) then {
            f"[STATUS] Wave: {GAME_WAVE} | HP: {GAME_PLAYER_HP} | Score: {GAME_SCORE} | Enemies: {GAME_ENEMIES_ALIVE}" call fn_log;
        };
    };
};

// Wait for game to finish:
private _finalScore = await _gameHandle;
f"Game returned final score: {_finalScore}" call fn_log;

"game-demo complete"

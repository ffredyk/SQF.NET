// ============================================================
// HostGame — Rich game-loop host demo for SQ#
// Demonstrates: multi-scheduler, custom commands, entities,
//               AI processing, scoring, game loop integration
// ============================================================

using System;
using System.Collections.Generic;
using SQSharp.Core;
using SQSharp.Host;

// ---- Game state ----
int _frameCount = 0;
double _gameTime = 0.0;
var _entities = new Dictionary<int, GameEntity>();
int _nextEntityId = 1;

// Simple game entity
record GameEntity(int Id, string Type, double X, double Y, double Hp)
{
    public double X { get; set; } = X;
    public double Y { get; set; } = Y;
    public double Hp { get; set; } = Hp;
}

// ---- Create host ----
var host = new SqHost();

// ---- Create named schedulers ----
var mainScheduler = host.MainScheduler;
var aiScheduler = host.CreateScheduler("AI", timeBudgetMs: 3.0);
var physicsScheduler = host.CreateScheduler("Physics", timeBudgetMs: 2.0);

// ============================================================
// Register game commands
// ============================================================

// --- Nular commands ---
host.RegisterNular("getFrameCount", () => new SqValue(_frameCount));
host.RegisterNular("getGameTime", () => new SqValue(_gameTime));
host.RegisterNular("getServerFps", () => new SqValue(60.0));

// --- Unary commands ---
host.RegisterUnary("getEntityCountOfType", arg =>
{
    var type = arg.AsString();
    int count = 0;
    foreach (var e in _entities.Values)
        if (e.Type == type) count++;
    return new SqValue(count);
});

host.RegisterUnary("getEntityHp", arg =>
{
    var id = (int)arg.AsNumber();
    return _entities.TryGetValue(id, out var e)
        ? new SqValue(e.Hp)
        : SqValue.Nil;
});

host.RegisterUnary("isEntityAlive", arg =>
{
    var id = (int)arg.AsNumber();
    return _entities.TryGetValue(id, out var e)
        ? new SqValue(e.Hp > 0)
        : SqValue.False;
});

host.RegisterUnary("sqDistance", arg =>
{
    // arg is [entityId, targetX, targetY]
    var arr = arg.AsArray();
    var id = (int)arr[0].AsNumber();
    if (!_entities.TryGetValue(id, out var e)) return new SqValue(-1.0);
    var tx = arr[1].AsNumber();
    var ty = arr[2].AsNumber();
    var dx = e.X - tx;
    var dy = e.Y - ty;
    return new SqValue(Math.Sqrt(dx * dx + dy * dy));
});

host.RegisterUnary("getAllEntityIds", _ =>
{
    var arr = new SqArray();
    foreach (var id in _entities.Keys)
        arr.PushBack(new SqValue(id));
    return new SqValue(arr);
});

// --- Binary commands ---
host.RegisterBinary("setEntityHp", (left, right) =>
{
    // left = entityId, right = [newHp]
    var id = (int)left.AsNumber();
    var newHp = right.AsArray()[0].AsNumber();
    if (_entities.TryGetValue(id, out var e))
    {
        e.Hp = Math.Max(0, newHp);
        return new SqValue(e.Hp);
    }
    return SqValue.Nil;
}, precedence: 4);

host.RegisterBinary("moveEntityTo", (left, right) =>
{
    // left = entityId, right = [x, y]
    var id = (int)left.AsNumber();
    var arr = right.AsArray();
    if (_entities.TryGetValue(id, out var e))
    {
        e.X = arr[0].AsNumber();
        e.Y = arr[1].AsNumber();
        return SqValue.True;
    }
    return SqValue.False;
}, precedence: 4);

host.RegisterBinary("damageEntity", (left, right) =>
{
    // left = entityId, right = amount
    var id = (int)left.AsNumber();
    var damage = right.AsNumber();
    if (_entities.TryGetValue(id, out var e))
    {
        e.Hp = Math.Max(0, e.Hp - damage);
        return new SqValue(e.Hp);
    }
    return new SqValue(-1.0);
}, precedence: 4);

host.RegisterBinary("addScore", (left, right) =>
{
    // left = current score, right = points to add
    return new SqValue(left.AsNumber() + right.AsNumber());
}, precedence: 6);

// --- Special: createEntity (returns entity ID) ---
host.RegisterUnary("createEntity", arg =>
{
    // arg = [type, x, y, hp]
    var arr = arg.AsArray();
    var type = arr[0].AsString();
    var x = arr[1].AsNumber();
    var y = arr[2].AsNumber();
    var hp = arr[3].AsNumber();
    var id = _nextEntityId++;
    _entities[id] = new GameEntity(id, type, x, y, hp);
    return new SqValue(id);
});

// --- Host output ---
host.OnPrint += msg => Console.WriteLine($"  [SCRIPT] {msg}");

// ============================================================
// Run game scripts
// ============================================================

// Main game logic script:
host.ExecuteString(@"
    // ---- Game init ----
    print ""=== GAME INIT ==="";
    global SCORE = 0;
    global PLAYER_ID = -1;

    // Create player unit
    PLAYER_ID = createEntity [""player"", 0, 0, 100];
    print f""Player entity #{PLAYER_ID} created"";

    // Create enemy units
    private _enemyIds = [];
    for ""_i"" from 1 to 5 do {
        private _ex = -50 + random 100;
        private _ey = -50 + random 100;
        private _eid = createEntity [""enemy"", _ex, _ey, 50];
        _enemyIds pushBack _eid;
        print f""Enemy #{_eid} spawned at [{_ex}, {_ey}]"";
    };

    // ---- AI Script (spawned on AI scheduler) ----
    // spawnOn unary form: right side = [schedulerName, code] array
    spawnOn [""AI"", {
        print ""[AI] Enemy AI controller starting..."";

        private _enemies = getAllEntityIds select { isEntityAlive _x && _x != PLAYER_ID };
        print f""[AI] Tracking {count _enemies} enemies"";

        for ""_tick"" from 1 to 10 do {
            if (count _enemies == 0) exitWith { print ""[AI] All enemies dead""; };

            // Each enemy moves toward player:
            {
                private _dist = sqDistance [_x, 0, 0];
                if (_dist > 5) then {
                    // Move a bit toward origin
                    moveEntityTo [_x, [0, 0]];
                } else {
                    print f""[AI] Enemy #{_x} at point-blank range! Attacking!"";
                    damageEntity PLAYER_ID 10;
                    print f""[AI] Player HP now: {getEntityHp PLAYER_ID}"";
                };
            } forEach _enemies;

            // Refresh enemy list (some may have died):
            _enemies = getAllEntityIds select { isEntityAlive _x && _x != PLAYER_ID };

            sleep 0.5;
        };
        print ""[AI] AI controller done."";
    }];

    // ---- Combat Script ----
    spawn {
        print ""[COMBAT] Player auto-attack started."";
        sleep 0.3;

        for ""_round"" from 1 to 8 do {
            if (!isEntityAlive PLAYER_ID) exitWith { print ""[COMBAT] Player dead — stopping""; };

            // Find nearest enemy:
            private _enemies = getAllEntityIds select {
                isEntityAlive _x && _x != PLAYER_ID
            };

            if (count _enemies == 0) exitWith {
                print ""[COMBAT] No enemies left!"";
            };

            // Attack nearest:
            private _target = _enemies select 0;
            damageEntity _target 15;
            print f""[COMBAT] Player attacks enemy #{_target} for 15 damage"";

            // Check if target died:
            private _targetHp = getEntityHp _target;
            if (_targetHp <= 0) then {
                print f""[COMBAT] Enemy #{_target} DESTROYED!"";
                SCORE = SCORE addScore 100;
                print f""[COMBAT] Score: {SCORE}"";
            };

            sleep 0.4;
        };
        print ""[COMBAT] Combat session ended."";
    };

    // ---- HUD Monitor ----
    spawn {
        print ""[HUD] Monitor started."";
        while { isEntityAlive PLAYER_ID } do {
            sleep 1;
            private _alive = count (getAllEntityIds select { isEntityAlive _x && _x != PLAYER_ID });
            private _hp = getEntityHp PLAYER_ID;
            print f""[HUD] Frame:{getFrameCount} | HP:{_hp} | Enemies:{_alive} | Score:{SCORE}"";
        };
        print ""[HUD] Player dead — monitor stopped."";
    };

    print ""=== All game scripts launched ==="";
");

// ============================================================
// Game loop
// ============================================================
Console.WriteLine("\n=== GAME LOOP STARTING ===\n");
const double FrameTime = 1.0 / 60.0; // ~60 FPS

for (int frame = 0; frame < 300; frame++)
{
    _frameCount = frame;
    _gameTime = frame * FrameTime;

    // Tick main scheduler
    host.TickMain();

    // Tick background schedulers (would be on their own threads in real app)
    host.TickScheduler("AI");
    host.TickScheduler("Physics");

    // Check if all scripts complete
    if (mainScheduler.ReadyCount == 0 && mainScheduler.WaitingCount == 0)
    {
        Console.WriteLine($"\nFrame {frame}: All main scripts completed.");
        break;
    }

    // Simulate frame timing
    System.Threading.Thread.Sleep((int)(FrameTime * 1000));
}

// ============================================================
// Final stats
// ============================================================
Console.WriteLine("\n=== GAME LOOP ENDED ===");
Console.WriteLine($"Total frames: {_frameCount}");
Console.WriteLine($"Game time: {_gameTime:F1}s");
Console.WriteLine($"Entities created: {_entities.Count}");
foreach (var (id, entity) in _entities)
    Console.WriteLine($"  Entity #{id}: {entity.Type} at ({entity.X:F0},{entity.Y:F0}) HP={entity.Hp:F0}");
Console.WriteLine($"Main scheduler — Ready: {mainScheduler.ReadyCount}, Waiting: {mainScheduler.WaitingCount}");

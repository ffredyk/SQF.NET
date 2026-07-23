# SQ# for .NET Developers

> How to embed SQ# scripting into your .NET application or game.

## NuGet Packages

| Package | What | Depends On |
|---|---|---|
| `SQSharp.Core` | `SqValue`, `SqType`, `SqArray`, bytecode types | Nothing |
| `SQSharp.Runtime` | VM + scheduler + all runtime | Core |
| `SQSharp.Compiler` | Lexer + parser + bytecode compiler | Core, Language |
| `SQSharp.Hosting` | High-level host API + 40+ StdLib commands | Core, Runtime, Compiler |
| `SQSharp.CLI` | Command-line tool (`dotnet sqf`) | All above |

Most apps only need `SQSharp.Hosting`.

## Quick Start

```bash
dotnet add package SQSharp.Hosting
```

```csharp
using SQSharp.Host;

// Create the scripting host
var host = new SqHost();

// Capture script output
host.OnPrint += msg => Console.WriteLine(msg);

// Run a script
host.ExecuteString(@"
    _arr = [1, 2, 3, 4, 5];
    _arr pushBack 6;
    hint format ['Array has %1 elements', count _arr];
");

// Tick the scheduler (in your game loop)
host.TickMain();
```

## Host Lifecycle

```csharp
var host = new SqHost();

// 1. Register custom commands (before spawning scripts)
host.RegisterNular("getPlayerCount", () => new SqValue((double)_players.Count));
host.RegisterUnary("getPlayerName", id => new SqValue(_players[(int)id.AsNumber()].Name));
host.RegisterBinary("damagePlayer", (id, amount) => {
    _players[(int)id.AsNumber()].Health -= amount.AsNumber();
    return SqValue.Nil;
}, precedence: 4, threadSafety: ThreadSafety.Isolated);

// 2. Spawn scripts
host.ExecuteString(@"
    _count = getPlayerCount;
    for '_i' from 0 to (_count - 1) do {
        _name = getPlayerName _i;
        systemChat format ['Player %1: %2', _i, _name];
    };
");

// 3. Game loop — tick every frame
while (_running)
{
    host.TickMain();           // Run scheduled scripts (3ms budget per tick)
    UpdateGameLogic();         // Your game logic
    Render();                  // Your rendering
}
```

## Command Registration

SQ# splits commands into two layers:

| Method | What | When to use |
|---|---|---|
| `RegisterCoreCommands()` | Math, string, array, logic, type checks. Always available. | Called automatically by `new SqHost()`. |
| `DeclareArmaCompatCommands()` | `hint`, `systemChat`, `diag_log`. Arma-specific output. | Opt-in. Skip for non-Arma hosts. |

```csharp
// Full Arma compat (default):
var host = new SqHost();  // or new SqHost(includeArmaCompat: true)

// Core only — register your own output:
var host = new SqHost(includeArmaCompat: false);
host.RegisterUnary("log", msg => { Logger.Log(msg.AsString()); return SqValue.Nil; });
host.RegisterUnary("announce", msg => { UIManager.ShowBanner(msg.AsString()); return SqValue.Nil; });
```

### By Arity

```csharp
// Nular — no arguments, returns a value
host.RegisterNular("getFPS", () => new SqValue(1.0 / deltaTime));

// Unary — one argument on the RIGHT
host.RegisterUnary("spawnEnemy", type => {
    SpawnEnemy(type.AsString());
    return SqValue.Nil;
});

// Binary — one on LEFT, one on RIGHT
host.RegisterBinary("damageEntity", (entity, amount) => {
    var id = (int)entity.AsNumber();
    _entities[id].Damage(amount.AsNumber());
    return SqValue.Nil;
}, precedence: 4);
```

### Thread Safety Declaration

```csharp
// ReadOnly — safe from any thread (pure computation, no side effects)
host.RegisterUnary("getPosition", entity => {
    return PackVector3(_entities[(int)entity.AsNumber()].Position);
}, ThreadSafety.ReadOnly);

// Isolated — only callable from owning scheduler (default)
host.RegisterBinary("setPosition", (entity, pos) => {
    _entities[(int)entity.AsNumber()].Position = UnpackVector3(pos);
    return SqValue.Nil;
}, precedence: 4, ThreadSafety.Isolated);

// Synchronized — has internal locking
host.RegisterBinary("addScore", (player, points) => {
    lock (_scoreLock) _scores[(int)player.AsNumber()] += (int)points.AsNumber();
    return SqValue.Nil;
}, precedence: 4, ThreadSafety.Synchronized);

// MainThread — only callable from main/UI thread
host.RegisterUnary("showDialog", name => {
    UIManager.ShowDialog(name.AsString());
    return SqValue.Nil;
}, ThreadSafety.MainThread);
```

## Working with SqValue

```csharp
// Creating values
SqValue num = new SqValue(42.0);
SqValue str = new SqValue("hello");
SqValue flag = SqValue.True;
SqValue nothing = SqValue.Nil;

// Implicit conversions from C# types
SqValue v1 = 42;           // double
SqValue v2 = true;         // bool
SqValue v3 = "text";       // string

// Reading values (typed)
double d = num.AsNumber();
string s = str.AsString();
bool b = flag.AsBool();

// Safe reads with defaults
double d2 = num.AsNumberOrDefault(0.0);
string s2 = str.AsStringOrDefault("fallback");

// Type checks
if (v1.IsNumber) { ... }
if (v2.Type == SqType.Boolean) { ... }

// Working with arrays
SqArray arr = new SqArray();
arr.PushBack(new SqValue(1.0));
arr.PushBack(new SqValue(2.0));
SqValue arrayVal = new SqValue(SqType.Array, arr);
```

## Multi-Scheduler Setup

```csharp
var host = new SqHost();

// Create additional schedulers (each on its own thread)
var aiScheduler = host.CreateScheduler("AI");
var ioScheduler = host.CreateScheduler("IO");

// Scripts can specify which scheduler to use
host.ExecuteString(@"
    // Run heavy AI computation on AI thread
    // Note: spawnOn creates a NEW scope — parent locals not accessible.
    // Pass data via left-side argument.
    _result = spawnOn ['AI', {
        params ['_data'];
        private _paths = processData(_data);
        return _paths;
    }];
    
    // Wait for result on main thread
    _paths = await _result;
");
```

## The Game Loop

```csharp
// Each frame:
void Update(float deltaTime)
{
    // 1. Update host's time
    // (handled internally by scheduler stopwatch)
    
    // 2. Tick the main scheduler
    host.TickMain();
    
    // 3. Background schedulers auto-tick on their own threads
    //    Or you can pump them manually:
    // aiScheduler.Tick();
    
    // 4. Check for completed scripts
    // (handle OnScriptEnd event for results)
}
```

## Error Handling

```csharp
// Runtime errors in scripts
host.OnError += (fiber, exception) => {
    Console.Error.WriteLine($"Script '{fiber.Name}' error: {exception.Message}");
    // Fiber is terminated
};

// Compile-time errors are thrown as exceptions from ExecuteString
try
{
    host.ExecuteString("_x = ;"); // syntax error
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Parse error: {ex.Message}");
}
```

## Thread Safety (For Host Developers)

SQ# handles thread safety implicitly. As a host developer, you just need to:

1. **Declare command thread safety** when registering (see above).
2. **Make your data structures thread-safe** if accessed from `Synchronized` commands.
3. **Everything else is automatic** — the VM enforces ownership, prevents cross-scheduler mutation, and provides freeze/channel/shared primitives.

```csharp
// Scripters use freeze for safe sharing:
host.ExecuteString(@"
    _data = [1, 2, 3, 4, 5];
    _frozen = freeze _data;           // immutable, readable from any scheduler
    
    // spawnOn creates NEW scope — pass _frozen as argument
    [_frozen] spawnOn ['AI', {
        params ['_frozenData'];
        _sum = _frozenData select 0;  // safe read-only access
    }];
");
```

## Complete Example — Simple Game

```csharp
using SQSharp.Core;
using SQSharp.Host;

var host = new SqHost();
var players = new Dictionary<int, Player>();

// Register game API
host.RegisterNular("playerCount", () => new SqValue((double)players.Count));
host.RegisterUnary("getPlayerName", id => new SqValue(players[(int)id.AsNumber()].Name));
host.RegisterUnary("getPlayerHealth", id => new SqValue(players[(int)id.AsNumber()].Health));
host.RegisterBinary("damagePlayer", (id, dmg) => {
    players[(int)id.AsNumber()].Health -= dmg.AsNumber();
    return SqValue.Nil;
}, 4);

host.OnPrint += msg => Console.WriteLine($"[GAME] {msg}");

// Load mission script
host.ExecuteString(File.ReadAllText("mission.sqf"), "mission");

// Game loop
var stopwatch = System.Diagnostics.Stopwatch.StartNew();
while (true)
{
    double dt = stopwatch.Elapsed.TotalSeconds;
    stopwatch.Restart();
    
    UpdateGameState(dt);
    host.TickMain();
    Render();
}
```

## CLI Tool

```bash
# Run a script
dotnet sqf run mission.sqf

# Interactive REPL
dotnet sqf repl

# Compile to bytecode listing
dotnet sqf compile script.sqf

# Show tokens (debug)
dotnet sqf lex script.sqf

# Show AST (debug)
dotnet sqf parse script.sqf
```

## Project Structure Reference

```
YourGame/
├── scripts/              # .sqf files
│   ├── mission.sqf
│   ├── ai/
│   └── ui/
├── YourGame.csproj       # <PackageReference Include="SQSharp.Hosting" />
└── Program.cs            # Host setup (see above)
```

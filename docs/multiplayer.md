# SQ# Multiplayer

> Scheduler = machine. Locality, remote execution, public variables —  
> SQ# multiplayer brings familiar Arma MP patterns to any .NET game engine.

---

## The Big Idea: Scheduler = Machine

In Arma, multiplayer is about **machines**: server, clients, headless clients. Each machine runs scripts, has its own state, and communicates via network messages.

In SQ#, a **scheduler** is the equivalent of an Arma machine. Each scheduler:
- Has its own **global namespace** (globals don't leak across schedulers)
- Runs its own **fiber queue** independently
- Has its own **machine identity** (server/client/headless)
- Can communicate with other schedulers via **remoteExec** and **publicVariable**

```
┌──────────────────────────────────────────────────────────────────┐
│                        HOST PROCESS                              │
│                                                                  │
│  ┌──────────────────────┐  ┌────────────────┐  ┌──────────────┐ │
│  │ Scheduler "Main"     │  │ Scheduler      │  │ Scheduler    │ │
│  │ ID = 1               │  │ "Client2" ID=3 │  │ "HC" ID=4    │ │
│  │                      │  │                │  │              │ │
│  │ isServer    = true   │  │ isServer=false │  │ isServer=f   │ │
│  │ isDedicated = true   │  │ isDedicated=f  │  │ isDed=f      │ │
│  │ hasInterface= false  │  │ hasInterface=t │  │ hasInter=f   │ │
│  │                      │  │                │  │              │ │
│  │ Role: SERVER          │  │ Role: CLIENT   │  │ Role: HC     │ │
│  └──────────┬───────────┘  └───────┬────────┘  └──────┬───────┘ │
│             │                      │                   │         │
│             └──────────┬───────────┴───────────────────┘         │
│                        │                                          │
│              ┌─────────▼─────────┐                               │
│              │  MESSAGE BUS      │  (🏠 host: network layer)    │
│              │  • remoteExec     │                               │
│              │  • publicVariable │                               │
│              │  • JIP queue      │                               │
│              └───────────────────┘                               │
└──────────────────────────────────────────────────────────────────┘
```

**Within a single process**, schedulers communicate via in-memory message passing.  
**Across processes** (🏠 host responsibility), the message bus is backed by a network layer (UDP/TCP).

---

## Machine Identity

Every scheduler knows what it is:

```sqf
isServer       // true → this scheduler is the authority (server)
isDedicated    // true → server running without UI (dedicated)
hasInterface   // true → this scheduler has a player/UI attached
isClient       // true → this is NOT the server (!isServer)
```

### Identity Combinations

| `isServer` | `isDedicated` | `hasInterface` | Arma Equivalent | SQ# Use Case |
|---|---|---|---|---|
| ✅ | ✅ | ❌ | Dedicated Server | Authoritative game state, AI, mission logic |
| ✅ | ❌ | ✅ | Player-Hosted Server | Single-player or listen server |
| ❌ | ❌ | ✅ | Player Client | Rendering, input, UI, local effects |
| ❌ | ❌ | ❌ | Headless Client | Offloaded AI, physics, pathfinding |
| ✅ | ❌ | ❌ | Headless Server (unusual) | Server-side processing without UI |

### Setting Identity (C# Host)

```csharp
var host = new SqHost();

// Dedicated server:
host.IsServer = true;
host.IsDedicated = true;
host.HasInterface = false;
host.DeclareMultiplayerCommands();

// Player client:
var client = new SqHost();
client.IsServer = false;
client.IsDedicated = false;
client.HasInterface = true;
client.DeclareMultiplayerCommands();
```

### Using Identity in Scripts

```sqf
// Server-only initialization:
if (isServer) then {
    global MISSION_TIME = 0;
    global ALL_UNITS = [];
    publicVariable "MISSION_TIME";
};

// Client-only UI:
if (hasInterface) then {
    [] spawn setupUI;
    player addEventHandler ["Respawn", { [] call onRespawn; }];
};

// Headless client offloading:
if (!hasInterface && !isDedicated) then {
    // I'm a headless client — take over AI processing
    while { true } do {
        { _x call processAI; } forEach allUnits;
        sleep 0.05;
    };
};
```

---

## Locality

### What Is Locality?

In multiplayer, every game object "lives" on one machine — the **owner**. Only the owner can change certain properties. Other machines see the results.

SQ# models this with **scheduler ownership**:

| SQ# Concept | Arma Equivalent |
|---|---|
| Scheduler ID | Machine/Client ID |
| `owner _object` | `owner _object` |
| Array ownership check | `local _object` |
| `ThreadSafety.Isolated` | LOCAL argument required |
| `ThreadSafety.ReadOnly` | GLOBAL argument (any machine can query) |

### Locality Rules

| Operation | Locality | Meaning |
|---|---|---|
| `_unit setDamage 1` | Global effect | All machines see the damage |
| `_unit addMagazine "30Rnd"` | LOCAL argument | Only owner can add inventory |
| `_unit setFace "Miller"` | Local effect | Change only visible on this machine |
| `getPos _unit` | GLOBAL argument | Any machine can query position |
| `alive _unit` | GLOBAL argument | Any machine can check |

### Registering Locality (C# Host)

```csharp
// LOCAL argument — only owning scheduler can call:
host.RegisterBinary("setDamage", (unit, damage) => {
    unit.Damage = damage.AsNumber();
    return SqValue.Nil;
}, precedence: 4, threadSafety: ThreadSafety.Isolated);

// GLOBAL argument — any scheduler can read:
host.RegisterUnary("getDamage", unit => new SqValue(unit.Damage),
    threadSafety: ThreadSafety.ReadOnly);
```

### Locality Errors

```sqf
// Fiber on scheduler "Client2" tries to damage a server-owned unit:
_unit setDamage 1;
// ❌ ThreadSafetyError: Command 'setDamage' is isolated to scheduler 'Main'.
//    Unit is owned by scheduler 1, called from scheduler 3.
```

**Fix**: Use `remoteExec` to run the command on the owning scheduler:

```sqf
[_unit, 1] remoteExec ["setDamage", owner _unit];
```

---

## Remote Execution

### `remoteExec` — Run Code on Another Machine

```sqf
// Syntax: [params] remoteExec [functionName, targets, isJIP]

// targets:
//   0  = all machines (broadcast)
//   2  = server only
//   -2 = all clients except server
//   3+ = specific scheduler ID

// Broadcast hint to everyone:
"Hello world!" remoteExec ["hint", 0];

// Run on server only:
[_unit, _killer] remoteExec ["handleKill", 2];

// Run on all clients except server:
[_weather] remoteExec ["setOvercast", -2];

// Run on specific client:
[_message] remoteExec ["systemChat", 3];

// Include JIP (Join-In-Progress) players:
[_initData] remoteExec ["initClient", 0, true];
```

### `remoteExecCall` — Remote Call (Returns Nothing)

Same syntax as `remoteExec`, but explicitly for fire-and-forget. Does not return a value.

```sqf
// Fire and forget — no return value expected:
[_unit, _pos] remoteExecCall ["moveTo", owner _unit];
```

### How `remoteExec` Works Internally

```
Scheduler "Client2" calls:
  [_unit, 1] remoteExec ["setDamage", 2]

1. Serializer packs: functionName="setDamage", args=[_unit,1], target=2
2. Message posted to target scheduler's inbox
3. Target scheduler (server) picks up message next Tick()
4. Server executes: [_unit, 1] call setDamage
5. (No return value — remoteExec is one-way)
```

### `remoteExec` Return Values (🏠 Host)

`remoteExec` is one-way by design. For request-response, use the channel pattern:

```sqf
// Client requests data from server:
_replyCh = channel;
["getScore", _playerId, _replyCh] remoteExec ["handleRequest", 2];
_result = _replyCh receive;   // wait for server response
```

### Security: Remote Execution Whitelist

Host can restrict which functions are remotely executable:

```csharp
host.AllowRemoteExec("hint");           // allow
host.AllowRemoteExec("systemChat");     // allow
host.AllowRemoteExec("setDamage");      // allow
// All other functions are blocked from remoteExec
```

Without whitelist configuration, all registered commands are remote-callable (like Arma's CfgRemoteExec defaults).

---

## Public Variables

### `publicVariable` — Broadcast Globals

```sqf
// Server sets a global and broadcasts it:
global SCORE = 100;
publicVariable "SCORE";
// All other schedulers now have SCORE = 100 in their globals
```

### `publicVariableServer` — Client → Server

```sqf
// Client sends a variable to the server only:
global PLAYER_READY = true;
publicVariableServer "PLAYER_READY";
```

### `publicVariableClient` — Server → Specific Client

```sqf
// Server sends to client #3:
global PERSONAL_SCORE = 500;
publicVariableClient ["PERSONAL_SCORE", 3];
```

### Public Variable Patterns

```sqf
// Mission start — broadcast initial state:
if (isServer) then {
    global MISSION_TIME = 0;
    global WEATHER = "clear";
    publicVariable "MISSION_TIME";
    publicVariable "WEATHER";
};

// Client reports ready:
if (hasInterface) then {
    global CLIENT_READY = true;
    publicVariableServer "CLIENT_READY";
};

// Server updates score for specific player:
[_playerId, _newScore] call {
    params ["_pid", "_score"];
    global FORMATTED_SCORE = _score;
    publicVariableClient ["FORMATTED_SCORE", _pid];
};
```

---

## Join In Progress (JIP)

Players who join mid-game need to catch up on game state.

```sqf
// Check if this client just joined:
if (didJIP) then {
    // Request state sync from server:
    ["requestSync", clientOwner] remoteExec ["handleJipSync", 2];
};

// Server handles JIP sync:
if (isServer) then {
    // Register code that JIP players receive:
    [player] remoteExec ["initPlayer", 0, true];  // isJip=true
};
```

### JIP Queue

The server maintains a JIP queue. When a client joins:
1. Server sends all queued `remoteExec` messages with `isJip=true`
2. Client processes them in order
3. Client catches up to current game state

```csharp
// Host configuration:
host.EnableJipQueue = true;          // maintain JIP queue
host.MaxJipQueueSize = 256;          // max stored messages
host.JipQueueExpiry = 600;           // expire after 10 minutes
```

---

## Multiplayer Patterns

### Pattern 1: Server-Authoritative Game State

```sqf
// Server owns all game state:
if (isServer) then {
    global GAME_STATE = createHashMap;
    GAME_STATE set ["round", 1];
    GAME_STATE set ["score_blue", 0];
    GAME_STATE set ["score_red", 0];
    publicVariable "GAME_STATE";
};

// Clients read:
_lb addEventHandler ["Killed", {
    if (isServer) then {
        GAME_STATE set ["score_red", (GAME_STATE get "score_red") + 1];
        publicVariable "GAME_STATE";
    };
}];
```

### Pattern 2: Client-Side Prediction + Server Correction

```sqf
// Client predicts movement locally:
_player setPos _predictedPos;

// Server corrects:
if (isServer) then {
    _actualPos = validatePosition(_player);
    [_player, _actualPos] remoteExec ["setPos", owner _player];
};
```

### Pattern 3: Headless Client AI

```sqf
// Headless client takes AI groups:
if (!hasInterface && !isDedicated) then {
    // Register as headless client:
    ["registerHC", clientOwner] remoteExec ["handleHC", 2];

    // Wait for AI assignment:
    _myGroups = channel;
    _myGroups receive; // server sends groups

    // Process AI:
    while { true } do {
        { _x call processGroupAI; } forEach _myGroups;
        sleep 0.05;
    };
};
```

### Pattern 4: Lobby / Pre-Game

```sqf
// Server maintains lobby:
global LOBBY_PLAYERS = [];
global GAME_STARTED = false;

// Client joins:
["join", playerName] remoteExec ["handleLobbyJoin", 2];

// Server broadcasts updated lobby:
publicVariable "LOBBY_PLAYERS";

// Server starts game:
GAME_STARTED = true;
publicVariable "GAME_STARTED";
```

---

## NetID System (🏠 Host)

Every network-relevant object gets a unique NetID:

```sqf
_netId = netId _unit;              // "2:1234" — scheduler 2, object 1234
_unit = objectFromNetId _netId;    // resolve back to object
```

Use cases:
- Referencing objects across schedulers without passing the object itself
- Network-efficient object identification
- JIP synchronization

---

## Host Implementation Guide

### Basic Server Host

```csharp
public class GameServer
{
    private SqHost _host;

    public void Start(bool dedicated)
    {
        _host = new SqHost(includeArmaCompat: true);
        _host.IsServer = true;
        _host.IsDedicated = dedicated;
        _host.HasInterface = !dedicated;
        _host.DeclareMultiplayerCommands();

        // Whitelist remotely executable commands:
        _host.AllowRemoteExec("hint");
        _host.AllowRemoteExec("setDamage");
        _host.AllowRemoteExec("setPos");

        // Enable JIP support:
        _host.EnableJipQueue = true;

        // Print handler:
        _host.OnPrint += msg => Console.WriteLine($"[SERVER] {msg}");

        // Load mission:
        _host.ExecuteString(File.ReadAllText("mission/init.sqf"));

        // Game loop:
        while (true)
        {
            _host.Tick();  // pumps all schedulers
            Thread.Sleep(16); // ~60 FPS
        }
    }
}
```

### Basic Client Host

```csharp
public class GameClient
{
    private SqHost _host;

    public void Start(string serverAddress)
    {
        _host = new SqHost(includeArmaCompat: true);
        _host.IsServer = false;
        _host.IsDedicated = false;
        _host.HasInterface = true;
        _host.DeclareMultiplayerCommands();

        _host.OnPrint += msg => Console.WriteLine($"[CLIENT] {msg}");

        // Connect to server (🏠 host networking):
        // _host.Connect(serverAddress);

        // Load client scripts:
        _host.ExecuteString(File.ReadAllText("mission/initClient.sqf"));

        // Game loop:
        while (true)
        {
            _host.Tick();
            Thread.Sleep(16);
        }
    }
}
```

---

## Command Reference

### Identity Commands

| Command | Type | Returns | Description |
|---|---|---|---|
| `isServer` | Nular | Boolean | Is this the server scheduler? |
| `isDedicated` | Nular | Boolean | Is this a dedicated server (no UI)? |
| `hasInterface` | Nular | Boolean | Does this scheduler have a player/UI? |
| `isClient` | Nular | Boolean | Is this NOT the server? |
| `didJIP` | Nular | Boolean | Did this client join in progress? |
| `clientOwner` | Nular | Number | This scheduler's ID |
| `owner` | Unary | Number | Which scheduler owns the argument? |
| `netId` | Unary | String | 🏠 Host | Network ID of object |
| `objectFromNetId` | Unary | Object | 🏠 Host | Resolve NetID to object |

### Remote Execution Commands

| Command | Type | Signature | Description |
|---|---|---|---|
| `remoteExec` | Binary | `[params, [cmd, targets, isJIP]]` | Execute command on target scheduler(s) |
| `remoteExecCall` | Binary | `[params, [cmd, targets]]` | Fire-and-forget remote execution |

### Public Variable Commands

| Command | Type | Signature | Description |
|---|---|---|---|
| `publicVariable` | Unary | `"varName"` | Broadcast global to all schedulers |
| `publicVariableServer` | Unary | `"varName"` | Send global to server |
| `publicVariableClient` | Binary | `["varName", clientId]` | Send global to specific client |

### Player Commands

| Command | Type | Returns | Description |
|---|---|---|---|
| `player` | Nular | Object | Player object on this scheduler |
| `allPlayers` | Nular | Array | All player objects |

---

## Current Status

| Feature | Status | Notes |
|---|---|---|
| Machine identity (`isServer`, etc.) | ✅ Implemented | Full support |
| `remoteExec` / `remoteExecCall` | ✅ Local | Same-process schedulers. Network layer = 🏠 host |
| `publicVariable` / `publicVariableServer` | ✅ Local | Same-process schedulers |
| `publicVariableClient` | ✅ Local | Same-process schedulers |
| `player` / `allPlayers` | 🏠 Host | Host must register with real objects |
| `didJIP` | 🏠 Host | Host must set based on connection state |
| `clientOwner` / `owner` | ✅ Implemented | Returns scheduler ID |
| `netId` / `objectFromNetId` | 🏠 Host | Requires networked object registry |
| Cross-process networking | 🏠 Host | UDP/TCP — host implements |
| JIP queue | 🏠 Host | Server-side queue — host implements |
| Remote exec whitelist | 🏠 Host | Host configures via `AllowRemoteExec()` |
| Lobby system | 🏠 Host | Game-specific — host implements |

---

## Testing Multiplayer Locally

### Multiple Schedulers in One Process

```csharp
// Server:
var server = new SqHost(includeArmaCompat: true);
server.IsServer = true;
server.IsDedicated = true;
server.HasInterface = false;
server.DeclareMultiplayerCommands();

// Client 1:
var client1 = new SqHost(includeArmaCompat: true);
client1.IsServer = false;
client1.HasInterface = true;
client1.DeclareMultiplayerCommands();

// Client 2:
var client2 = new SqHost(includeArmaCompat: true);
client2.IsServer = false;
client2.HasInterface = true;
client2.DeclareMultiplayerCommands();

// Wire message bus between them:
server.MessageBus.Connect(client1.MessageBus);
server.MessageBus.Connect(client2.MessageBus);
client1.MessageBus.Connect(server.MessageBus);
client2.MessageBus.Connect(server.MessageBus);

// Now remoteExec and publicVariable work across schedulers.
```

### Testing Identity-Dependent Code

```sqf
// test_mp.sqf:
assert (isServer == true);           // run with server host
assert (hasInterface == false);      // dedicated

if (isServer) then {
    global TEST_VAR = 42;
    publicVariable "TEST_VAR";
};
```

---

## FAQ

### Q: Can I run multiple schedulers in the same process?
**A:** Yes. That's the default model. Each scheduler gets its own namespace, fiber queue, and identity. `remoteExec` and `publicVariable` work between schedulers in the same process via in-memory message passing.

### Q: How do I connect to a remote server?
**A:** 🏠 Host responsibility. The host implements networking (UDP/TCP). SQ# provides `remoteExec`/`publicVariable` dispatch — the host serializes and sends messages across the wire. See [scope-and-roadmap.md](scope-and-roadmap.md).

### Q: Do I need to manually sync state?
**A:** Not with `publicVariable`. Once broadcast, the variable is set on all schedulers. For complex state (arrays, hashmaps), use `freeze` before `publicVariable` — frozen data is immutable and safe to share.

### Q: Can a headless client run multiple schedulers?
**A:** Yes. A headless client host can create multiple schedulers for different AI groups, physics processing, etc. Each gets its own 3ms budget.

### Q: How does `remoteExec` differ from Arma?
**A:** In the current SQ# implementation (same-process), `remoteExec` delivers messages synchronously within the same `Tick()`. This matches Arma's behavior where `remoteExec` can be immediate for same-machine calls. Cross-process `remoteExec` will be asynchronous like Arma.


# SQ# Scope & Roadmap

> What SQ# owns. What the host owns. What's coming. What's not.

---

## The Rule

**SQ# is a scripting language + runtime.** It provides:
- Lexer, parser, compiler
- Bytecode VM with cooperative scheduling
- Multi-scheduler architecture with thread safety
- Standard library (math, strings, arrays, types)
- CLI tooling (lex, parse, compile, run, repl)

**SQ# does NOT provide:**
- Networking (UDP/TCP, message serialization, client-server protocol)
- Game engine integration (Unity, Godot, Unreal — separate packages)
- UI system (dialogs, displays, controls)
- Object system (vehicles, units, weapons — host defines these)
- Physics, rendering, audio
- File system access (host may expose via commands)

The hosting project implements these. SQ# gives you the scripting engine — you build the world around it.

---

## Planned Features — In Scope (SQ# Owns)

### Language

| Feature | Status | Notes |
|---|---|---|
| Lazy evaluation `a && {b}` / `a \|\| {b}` | ✅ Done | Short-circuit with code blocks. `JumpIfFalse` + `JumpIfTrue` + `Dup` in compiler. |
| Range operator `..` (PrecRange=5) | 🔜 Planned | `for "_i" from 0 to 10` already works. `0..10` as standalone range. |
| `switch` / `case` / `default` | ✅ Done | Parser, AST (`SwitchDoNode`), compiler (`CompileSwitchDo`), VM tested. |
| String interpolation `f"Hello {name}"` | ⚠️ Maybe | Parser + compiler work. Nice-to-have. |
| `try`/`catch`/`throw` — full error propagation | ✅ Done | `TryBegin`/`TryEnd`/`Throw` opcodes + `CompileTryCatch` in compiler. |
| `terminate` (fiber cancellation) | ✅ Done | `SqFiber.Terminate()` + `terminate` unary command registered. |

### Compiler

| Feature | Status | Notes |
|---|---|---|
| `.sqfc` binary serialization | ✅ Done | Serialize `BytecodeChunk` to binary. Load without re-parsing. |
| Constant folding | ✅ Done | `1 + 2` → `3` at compile time. `TryConstantFold` in compiler. |
| Dead code elimination | 🔜 Planned | Remove unreachable branches after `return`/`throw`. |

### VM & Runtime

| Feature | Status | Notes |
|---|---|---|
| DAP debugger protocol | 🔜 Planned | Breakpoints, step-in/over, variable inspection, call stack. |
| Fiber cancellation (`terminate`) | ✅ Done | `terminate` unary command + `SqScheduler.TerminateHandle()`. |
| `preprocessFileLineNumbers` | 🔜 Planned | SQ# preprocessor exists (`SQSharp.Preprocessor`). Needs CLI + runtime integration. |
| `execVM` — load+compile+spawn file | ✅ Done | Registered in `SqHost.RegisterCoreCommands()` via `File.ReadAllText` + `ExecuteString`. |
| `callExtension` (native interop) | 🔜 Planned | .NET host interop via `RegisterCommand` is the replacement. C-friendly FFI for non-.NET hosts. |
| Stack trace on error | ✅ Done | `MakeError()` includes source file, line, and column. |

### Standard Library

| Feature | Status | Notes |
|---|---|---|
| `Math` commands (`sin`, `cos`, `atan2`, `exp`, `log`, `pow`, `random`, `abs`, `floor`, `ceil`, `round`, `sqrt`, `asin`, `acos`, `tan`) | ✅ Done | Wrap `System.Math`. Registered in `SqHost.RegisterCoreCommands()`. |
| `selectRandom`, `selectRandomWeighted` | ✅ Done | Registered in `SqHost.RegisterCoreCommands()`. |
| `param` / `params` (default values) | ✅ Done | Compiler inlines destructuring from `_this` array. `CompileParams()` in compiler. |
| `createHashMap` / `createHashMapFromArray` | ✅ Done | Both constructors registered in `SqHost.RegisterCoreCommands()`. |
| `BIS_fnc_*` compatibility layer | ❌ Out of scope | Host provides mission functions. |
| `diag_*` debugging commands | ⚠️ Partial | `diag_tickTime` done. Others not yet. |

### Scheduler

| Feature | Status | Notes |
|---|---|---|
| `spawnOn` — cross-scheduler spawning | ✅ Done | |
| `await` — promise-based suspension | ✅ Done | |
| `sleep` / `await` / `timeout` | ✅ Done | `waitUntil` use case covered by `await _handle timeout seconds`. |
| `currentScheduler` / `clientOwner` | ✅ Done | |
| `scheduler` / `isSchedulerLocal` | ✅ Done | |
| `allSchedulers` / `schedulerName` / `schedulerExists` | ✅ Done | |
| `schedulerBudget` / `setSchedulerBudget` | ✅ Done | |
| `fiberCount` / `readyFiberCount` / `waitingFiberCount` | ✅ Done | |
| `sendTo` — transfer array ownership | ✅ Done | |
| `freeze` / `thaw` — immutable sharing | ✅ Done | |
| `shared` — CAS-based atomic variables | ✅ Done | |
| `Channel` — message passing | ✅ Done | |
| Fiber priority (high/low) | 🔜 Planned | Priority queue instead of FIFO. |
| Fiber timeout (`spawnWithTimeout`) | 🔜 Planned | Auto-terminate after N seconds. |

### CLI Tooling

| Feature | Status | Notes |
|---|---|---|
| `lex` / `parse` / `compile` / `run` / `repl` | ✅ Done | |
| `.sqfc` compile to binary | 🔜 Planned | `sqsharp compile --binary -o script.sqfc` |
| `--watch` mode (re-run on file change) | 🔜 Planned | Dev workflow. |
| Syntax highlighting / LSP | 🔜 Planned | VS Code extension. |

---

## Host Responsibility (NOT in SQ#)

These are explicitly OUT of scope. The hosting project implements them.

### Networking

| Feature | Who | Notes |
|---|---|---|
| Cross-process UDP/TCP message bus | 🏠 Host | SQ# gives `remoteExec` local dispatch. Host adds network layer. |
| Client-server protocol | 🏠 Host | Handshake, authentication, state sync. |
| Network serialization | 🏠 Host | How `SqValue` travels over wire. |
| `netId` / `objectFromNetId` | 🏠 Host | Requires networked object registry. |
| JIP queue persistence | 🏠 Host | SQ# provides JIP flag on `remoteExec`. Queue storage is host's. |
| Lobby / matchmaking | 🏠 Host | Game-specific. |
| `publicVariable` network broadcast | 🏠 Host | SQ# dispatches locally. Host serializes + sends. |
| `host.Connect("192.168.1.100:2302")` | 🏠 Host | Host networking API. Not SQ# core. |

### Game Engine Integration

| Feature | Who | Notes |
|---|---|---|
| Unity `MonoBehaviour` host | 🏠 Host | Separate NuGet package `SQSharp.Unity`. |
| Godot / Unreal / Stride integration | 🏠 Host | Community or separate packages. |
| Coroutine bridge (Unity `IEnumerator`) | 🏠 Host | Map SQ# fibers to Unity coroutines. |
| Physics commands (`setVelocity`, `addForce`) | 🏠 Host | Engine-specific. |
| Rendering commands (`drawLine3D`, `drawIcon3D`) | 🏠 Host | Engine-specific. |

### Object System

| Feature | Who | Notes |
|---|---|---|
| `player` / `allPlayers` | 🏠 Host | Host registers commands returning actual player objects. |
| `allUnits` / `vehicles` / `allDead` | 🏠 Host | Host defines entity registry. |
| `createVehicle` / `createUnit` / `deleteVehicle` | 🏠 Host | Host defines object lifecycle. |
| `setDamage` / `setPos` / `setDir` / `setVelocity` | 🏠 Host | Host defines entity properties. |
| `addEventHandler` / `removeEventHandler` | 🏠 Host | Host defines event system. |
| `side` / `faction` / `group` / `rank` | 🏠 Host | Host defines game logic. |

### UI System

| Feature | Who | Notes |
|---|---|---|
| `createDialog` / `closeDialog` | 🏠 Host | UI is engine-specific. |
| `ctrlSetText` / `ctrlSetPosition` | 🏠 Host | Host defines UI controls. |
| `hint` / `systemChat` / `titleText` | 🏠 Host | Output formatting is host's choice. |
| Display / control event handlers | 🏠 Host | Host defines event propagation. |

### Mission / Config System

| Feature | Who | Notes |
|---|---|---|
| `configFile` / `missionConfigFile` | 🏠 Host | Config hierarchy is an Arma concept. Host may implement equivalent. |
| `missionNamespace` / `uiNamespace` / `parsingNamespace` | 🏠 Host | Namespace storage. Host provides. |
| `CfgFunctions` / `CfgVehicles` | 🏠 Host | Arma-specific config classes. |
| `Description.ext` parsing | 🏠 Host | Host defines mission format. |

### File I/O & System

| Feature | Who | Notes |
|---|---|---|
| `loadFile` / `saveFile` / `preprocessFile` | 🏠 Host | Host decides file system access policy. |
| `execVM` from file | 🏠 Host | Host provides file resolution + caching. |
| Environment variables / command line | 🏠 Host | Host passes startup config. |
| Database access | 🏠 Host | Host may register DB commands. |

---

## Quick Decision Table

| Item | SQ#? | Host? | Reasoning |
|---|---|---|---|
| Lazy `&&` / `\|\|` | ✅ SQ# | — | VM optimization |
| `.sqfc` binary format | ✅ SQ# | — | Compiler feature |
| DAP debugger | ✅ SQ# | — | Tooling |
| `terminate` fiber | ✅ SQ# | — | Scheduler feature |
| `switch` / `case` | ✅ SQ# | — | Language feature |
| `params` with defaults | ✅ SQ# | — | StdLib |
| Constant folding | ✅ SQ# | — | Compiler |
| `execVM` runtime | ✅ SQ# | — | Runtime |
| Stack traces | ✅ SQ# | — | VM |
| UDP networking | — | 🏠 Host | Transport layer |
| `netId` / `objectFromNetId` | — | 🏠 Host | Needs network registry |
| JIP queue | — | 🏠 Host | Needs persistence |
| Lobby | — | 🏠 Host | Game-specific |
| `player` / `allPlayers` | — | 🏠 Host | Entity registry |
| `createVehicle` | — | 🏠 Host | Object system |
| `createDialog` | — | 🏠 Host | UI system |
| `configFile` | — | 🏠 Host | Arma concept |
| `preprocessFile` | — | 🏠 Host | File system policy |
| Unity integration | — | 🏠 Host | Separate package |
| `BIS_fnc_*` | — | 🏠 Host | Mission functions |

---

## Version Roadmap

### v0.7 — Current
- [x] Lexer, Pratt parser, bytecode compiler, stack VM
- [x] Cooperative fiber scheduler with time-budgeted `Tick()`
- [x] Multi-scheduler architecture (`spawnOn`, `sendTo`, `scheduler`)
- [x] Thread safety: ownership tracking, `freeze`/`thaw`, `shared` (CAS), `Channel` (SPSC)
- [x] `try`/`catch`/`throw` with full error propagation + stack traces
- [x] Lazy `&&`/`||` with code-block short-circuit
- [x] `switch`/`case`/`default`
- [x] `params` with defaults (compiler-inlined destructuring)
- [x] `call`/`spawn` with args, `await`, `sleep`, `timeout`, `terminate`
- [x] Constant folding (`TryConstantFold` in compiler)
- [x] Math commands: `sin`, `cos`, `tan`, `asin`, `acos`, `atan2`, `sqrt`, `abs`, `exp`, `log`, `floor`, `ceil`, `round`, `pow`, `random`
- [x] `selectRandom` / `selectRandomWeighted`
- [x] `createHashMap` / `createHashMapFromArray`
- [x] `execVM` runtime command
- [x] `.sqfc` binary serializer + CLI compile (`sqsharp compile --binary`)
- [x] `compile` runtime command
- [x] Array ops: `pushBack`, `select`, `deleteAt`, `deleteRange`, `resize`, `append`, `reverse`, `sort`, `find`, `in`
- [x] String ops: `parseNumber`, `toArray`, `toString`, `splitString`, `joinString`, `toLower`, `toUpper`, `trim`
- [x] Type checks: `isNil`, `str`, `format`, `typeName`
- [x] CLI: `lex`, `parse`, `compile`, `run`, `repl`, `serialize`, `deserialize`
- [x] Host API: `SqHost`, `SaveState`/`LoadState`, command registration with thread safety levels

### v0.8 — Compiler & Runtime
- [ ] Dead code elimination (unreachable after `return`/`throw`)
- [ ] `preprocessFileLineNumbers` integration (`SQSharp.Preprocessor`)
- [ ] Range operator `..` (standalone range expression)
- [ ] Fiber priority (high/low queues)
- [ ] Fiber timeout (`spawnWithTimeout`)

### v0.9 — Tooling & Polish
- [ ] DAP debugger protocol (breakpoints, step-in/over, variable inspection, call stack)
- [ ] VS Code extension (syntax highlighting, LSP)
- [ ] `--watch` mode (re-run on file change)
- [ ] String interpolation `f"Hello {name}"`
- [ ] `callExtension` (native interop / FFI)
- [ ] `diag_*` diagnostics suite

### v1.0 — Stable
- [ ] NuGet packages published
- [ ] API documentation (docfx)
- [ ] Performance benchmarks
- [ ] Backward compatibility guarantee

### Future / Separate
- Unity package (`SQSharp.Unity`)
- Network layer reference implementation
- Community host templates

---

## Principles

1. **SQ# is embeddable.** Any .NET app can host it. No dependencies on Unity, Godot, or any engine.
2. **Host owns the world.** SQ# provides the scripting engine. Objects, networking, UI — host defines them via registered commands.
3. **SQF compatibility is a goal, not a constraint.** Where SQF is broken (undefined vars, precedence), SQ# fixes it. Where SQF is Arma-specific (config, vehicles, sides), the host implements equivalents. SQ# preserves SQF's nil-deletes-variable semantics.
4. **Thread safety is built-in, not bolted-on.** Ownership, freeze/thaw, channels, shared — all in the runtime. No opt-in flags.
5. **Tooling matters.** A language without a debugger is half a language. DAP support is a priority.

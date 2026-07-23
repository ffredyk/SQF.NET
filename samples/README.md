# SQ# Samples

Comprehensive samples demonstrating the SQ# scripting language and host integration.

## .sqf Script Samples

| File | Topic | What It Covers |
|---|---|---|
| [`basics.sqf`](basics.sqf) | Variables & Types | Literals, variables, assignment, arithmetic, comparison, logical operators, nil handling, type annotations, verbatim/multi-line strings |
| [`control-flow.sqf`](control-flow.sqf) | Control Flow | if/else/else-if, while-do, for-from-to-step, for-array-form, forEach, switch-case-default, exitWith, short-circuit evaluation |
| [`arrays.sqf`](arrays.sqf) | Arrays | Creation, access (select/#/[]), mutation (pushBack/append/set/deleteAt/resize), copy (shallow/deep), functional ops (apply/select/findIf), sort/reverse, freeze/thaw, intersection |
| [`strings.sqf`](strings.sqf) | Strings | Creation, escape sequences, verbatim strings, multi-line, interpolation (f"..."), format, str, concatenation, search, case conversion, splitString/joinString, toString/toArray, optimisation |
| [`functions.sqf`](functions.sqf) | Functions & Code | Code blocks as values, call vs spawn vs execVM, compile, params/param, function factories (closures), recursion, higher-order functions, global functions, compose |
| [`async-promises.sqf`](async-promises.sqf) | Async & Promises | sleep, spawn (returns ScriptHandle), scriptDone, await (with/without timeout), waitUntil, continueWith, terminate, PromiseAll/Race/Any, progress reporting, _thisScript |
| [`concurrency.sqf`](concurrency.sqf) | Multithreading | spawnOn (named schedulers), spawnParallel (thread pool), scheduler-local globals, Freeze (immutable snapshots), Channel<T> (lock-free messaging), Shared<T> (CAS atomics), ownership model, thread safety |
| [`error-handling.sqf`](error-handling.sqf) | Error Handling | try/catch, typed catches, throw, structured error objects (_error), nil safety, array safety, error propagation in promises, defensive coding patterns |
| [`data-structures.sqf`](data-structures.sqf) | HashMaps & Namespaces | createHashMap, set/get/deleteAt, key types, forEach iteration, createHashMapFromArray, nested HashMaps, Namespaces (missionNamespace, setVariable/getVariable), practical patterns (struct, table, method-dispatch, cache, set, frequency counter) |
| [`optimisation.sqf`](optimisation.sqf) | Performance | Do's and don'ts: string building, array building, loop optimization, early exit, pre-allocation, caching, HashMap vs array scan, FrozenArray for sharing, scheduler-aware design |
| [`game-demo.sqf`](game-demo.sqf) | Game Simulation | Full game-like demo: waves of enemies, AI scheduling (spawnOn ["AI", {...}]), combat system, scoring, HUD monitor, PromiseAll for wave completion |

## C# Host Samples

| Directory | What It Shows |
|---|---|
| [`HostMinimal/`](HostMinimal/) | Minimal host: create host, register custom commands (nular/unary/binary with precedence), execute scripts, schedule game loop with frame-by-frame ticking |
| [`HostGame/`](HostGame/) | Rich game-loop host: multi-scheduler (Main + AI + Physics), custom entity system, game commands (create/damage/move entities, distance calc), full game loop with scripted AI, combat, and HUD |

## Legacy Test Samples

| File | Purpose |
|---|---|
| `comp2.sqf` | Parser test: `compile` returning code value |
| `comp3.sqf` | Parser test: `call` with compiled code |
| `comp4.sqf` | Parser test: bare `call compile` expression |

## Running Samples

```bash
# Run a single .sqf script:
dotnet run --project src/SQSharp.CLI -- run samples/basics.sqf

# Run the minimal host demo:
dotnet run --project samples/HostMinimal

# Run the game host demo:
dotnet run --project samples/HostGame
```

## Sample Progression

1. **New to SQ#?** Start with [`basics.sqf`](basics.sqf) → [`control-flow.sqf`](control-flow.sqf) → [`arrays.sqf`](arrays.sqf) → [`strings.sqf`](strings.sqf)
2. **Know SQF already?** Jump to [`functions.sqf`](functions.sqf) → [`async-promises.sqf`](async-promises.sqf) — see what changed
3. **Building a game host?** Study [`HostMinimal/`](HostMinimal/) → [`HostGame/`](HostGame/) → [`game-demo.sqf`](game-demo.sqf)
4. **Need performance?** Read [`optimisation.sqf`](optimisation.sqf)
5. **Using multithreading?** Read [`concurrency.sqf`](concurrency.sqf) — understand Freeze/Channel/Shared

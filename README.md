# SQ# (SQF Sharp)

**Bring SQF scripting to any .NET project.**

SQ# is a modernized, embeddable reimplementation of Arma 3's SQF scripting language
for .NET 10. It brings the familiar SQF syntax, operator-based design, and cooperative
scheduling model to any .NET application — game engines, tools, servers, or CLI apps.

## Status

**v0.7** — Language complete. Lexer, Pratt parser, bytecode compiler, stack VM,
cooperative fiber scheduler, multi-scheduler thread safety, CLI tooling, and
100+ commands all working. 122 tests passing. See [Scope & Roadmap](docs/scope-and-roadmap.md).

## Quick Start

```bash
# Install (.NET 10 required)
git clone https://github.com/ffredyk/SQF.NET
cd SQF.NET
dotnet build

# Run a script
dotnet run --project src/SQSharp.CLI -- run samples/basics.sqf

# Open REPL
dotnet run --project src/SQSharp.CLI -- repl

# Compile to binary .sqfc
dotnet run --project src/SQSharp.CLI -- compile samples/basics.sqf --binary -o basics.sqfc

# Run tests
dotnet test
```

## Quick Example

```sqf
// Variables and arithmetic
private _x = 10;
private _y = _x * 3 + 5;       // 35

// Arrays and commands
private _arr = [1, 2, 3, 4, 5];
_arr pushBack 6;                // append — returns index
private _third = _arr select 2; // 3
private _len = count _arr;      // 6

// Control flow
if (_x > 5) then {
    systemChat "x is big";
} else {
    systemChat "x is small";
};

// Loops
for "_i" from 0 to 4 do {
    _arr select _i;
};

// Switch
switch (_x) do {
    case 10: { systemChat "ten" };
    default { systemChat "other" };
};

// Error handling
try {
    _result = 100 / 0;
} catch {
    systemChat str _exception;
};

// Functions with params and defaults
private _sum = [10, 20] call {
    params ["_a", ["_b", 99]];  // _b defaults to 99
    _a + _b                     // returns 30
};

// Async concurrency
private _handle = spawn {
    sleep 2.5;
    systemChat "done";
};
private _result = await _handle timeout 5;

// Thread-safe shared state
shared _counter = 0;
_counter add 1;                 // atomic increment
private _val = get _counter;

// HashMap
private _map = createHashMapFromArray ["key1", 100, "key2", 200];
_map set ["key3", 300];
private _v = _map get "key1";   // 100
```

## Key Features

- **SQF-compatible syntax** — familiar `call`/`spawn`/`execVM`, code-as-data, operator precedence
- **Modern enhancements** — try/catch/throw, switch/case, params with defaults, lazy `&&`/`||`, HashMap
- **Bytecode VM** — 25 opcodes, stack-based, constant folding, binary `.sqfc` serialization
- **Cooperative scheduling** — fibers with time budget, multi-scheduler architecture
- **Implicit thread safety** — scheduler-local globals, ownership tracking, freeze/channel/shared primitives
- **Extensible host API** — register custom commands by arity, precedence, and thread safety level
- **Promise system** — Script Handles as async/await with `timeout` combinator
- **Embeddable** — single library, host in any .NET 10 application

## Current Capabilities (v0.7)

### Language

| Feature | Details |
|---|---|
| Variables | `_local`, `global`, assignment as expression |
| Literals | Number, string, boolean, nil, array `[a,b]`, code `{...}` |
| Operators | `+` `-` `*` `/` `%` `^`, `==` `!=` `<` `>` `<=` `>=` |
| Logic | `&&` `||` `!` with lazy short-circuit on code blocks |
| Control flow | `if/then/else`, `while`, `for..from..to`, `for [{},{},{}]` |
| Switch | `switch/case/default` with expression matching |
| Error handling | `try/catch/throw` with full error propagation + stack traces |
| Functions | `call`/`spawn` with args, `params` with defaults, `return` |
| Comments | `// line`, `/* block */` |

### Runtime Commands

| Category | Commands |
|---|---|
| Math | `sin` `cos` `tan` `asin` `acos` `atan2` `sqrt` `abs` `exp` `log` `floor` `ceil` `round` `pow` `random` |
| Array | `pushBack` `select` `deleteAt` `deleteRange` `resize` `append` `reverse` `sort` `find` `in` `count` |
| String | `parseNumber` `toArray` `toString` `splitString` `joinString` `toLower` `toUpper` `trim` `find` |
| HashMap | `createHashMap` `createHashMapFromArray` `get` `set` |
| Random | `random` `selectRandom` `selectRandomWeighted` |
| Type | `isNil` `str` `format` `typeName` `compile` |
| Diagnostics | `diag_tickTime` `hint` `systemChat` `diag_log` |

### Concurrency & Thread Safety

| Feature | Details |
|---|---|
| Scheduling | Cooperative fibers with 3ms time budget per `Tick()` |
| Multi-scheduler | `spawnOn` cross-scheduler spawning, `sendTo` array transfer |
| Async | `await`, `sleep`, `timeout`, `terminate` |
| Ownership | `scheduler`, `isSchedulerLocal`, automatic tracking |
| Immutable sharing | `freeze`/`thaw` — zero-copy read-only sharing across schedulers |
| Atomic | `shared` — CAS-based synchronized variables (`add`, `sub`, `get`, `set`, `compareSwap`) |
| Channels | SPSC message passing between schedulers |
| Scheduler query | `allSchedulers`, `schedulerName`, `schedulerExists`, `schedulerStats`, `fiberCount` |

### CLI Tooling

| Command | Description |
|---|---|
| `sqf lex <file>` | Token dump |
| `sqf parse <file>` | AST tree |
| `sqf compile <file> [--binary]` | Bytecode listing or `.sqfc` binary output |
| `sqf run <file>` | Execute script |
| `sqf repl` | Interactive read-eval-print loop |
| `sqf serialize <file>` | Run + dump binary serialized result |
| `sqf deserialize <file>` | Read `.sqfc` binary back |

### Host API

| Feature | Details |
|---|---|
| `SqHost` | Central API — parse, compile, spawn, state save/load |
| Command registration | By arity (nular/unary/binary), precedence, and thread safety level |
| Host types | `RegisterType<T>()` for host-defined object types |

## Documentation

| Document | For | Description |
|---|---|---|
| [SQ# Enhancements](docs/sqsharp-enhancements.md) | 👥 Everyone | **Master list** — everything SQ# brings over SQF |
| [Scope & Roadmap](docs/scope-and-roadmap.md) | 👥 Everyone | What SQ# owns, what the host owns, what's coming |
| [SQ# for SQF Scripters](docs/for-sqf-scripters.md) | 🎮 SQF users | Migration guide — what changed, how to script |
| [SQ# for .NET Developers](docs/for-dotnet-devs.md) | 💻 .NET devs | Embedding guide, NuGet, host API |
| [Quick Reference](docs/quick-reference.md) | 📋 Everyone | One-page language reference card |
| [Command Reference](docs/commands.md) | 📋 Everyone | Complete reference for all 100+ internal commands |
| [Multithreading](docs/multithreading.md) | 🎮 SQF users | Fibers, schedulers, channels, shared, freeze/thaw |
| [Multiplayer](docs/multiplayer.md) | 🎮 SQF users | Locality, remoteExec, publicVariable, JIP, patterns |
| [Code Optimisation](docs/optimisation.md) | 🎮 SQF users | Performance tips, do/don't, scheduling |
| [Benchmark Results](docs/benchmarks.md) | 📊 Everyone | SQ# vs Arma 3 performance, explanations, constraints |
| [Language Specification](docs/language-spec.md) | 🔧 Contributors | Syntax, operators, precedence, control structures |
| [Type System](docs/types.md) | 🔧 Contributors | Data types, magic types, nil/void semantics |
| [Scheduler & Thread Safety](docs/scheduler-threads.md) | 🔧 Contributors | Execution model, fiber scheduling, implicit safety |
| [Promise System](docs/promises.md) | 🔧 Contributors | Script handles, async/await, combinators |
| [Array Semantics](docs/arrays.md) | 🔧 Contributors | Array behavior, quirks, SQ# fixes |
| [Implementation Plan](docs/plan.md) | 🔧 Contributors | Full architecture, decisions, milestones |

## Project Structure

```
SQF.NET/
├── src/
│   ├── SQSharp.Core/            # SqValue, SqType, SqArray, SqHashMap, SqCode, SqError, bytecode types
│   ├── SQSharp.Language/        # Lexer, Pratt Parser (20+ AST nodes), Token types
│   ├── SQSharp.Compiler/        # AST → Bytecode (single-pass recursive walk)
│   ├── SQSharp.VM/              # Stack VM (25 opcodes), builtin commands, ISqScheduler
│   ├── SQSharp.Scheduler/       # SqFiber, SqScheduler, ScriptHandle, cooperative scheduling
│   ├── SQSharp.Host/            # SqHost — central host API, command registration, save/load
│   ├── SQSharp.StdLib/          # Standard library commands
│   ├── SQSharp.CLI/             # CLI: lex, parse, compile, run, repl, serialize, deserialize
│   └── SQSharp.Preprocessor/    # Legacy SQF preprocessor (opt-in)
├── tests/
│   ├── SQSharp.Core.Tests/      # SqValue, SqArray unit tests
│   ├── SQSharp.Language.Tests/  # Lexer, Parser unit tests
│   ├── SQSharp.Compiler.Tests/  # Compiler unit tests
│   └── SQSharp.VM.Tests/        # Execution tests (122 total, 0 failures)
├── samples/                     # 14 .sqf example scripts + 2 host embedding samples
└── docs/                        # 14 documentation guides
```

## Architecture

```
Source Code
    │
    ▼
┌──────────┐    ┌──────────┐    ┌──────────┐
│  Lexer   │───▶│  Parser  │───▶│ Compiler │
│ (tokens) │    │  (AST)   │    │(bytecode)│
└──────────┘    └──────────┘    └──────────┘
                                      │
                                      ▼
                                ┌──────────┐
                                │  SqVm    │
                                │ (stack)  │
                                └──────────┘
                                      │
                    ┌─────────────────┼─────────────────┐
                    ▼                 ▼                 ▼
              ┌──────────┐    ┌──────────┐    ┌──────────┐
              │Scheduler │    │Scheduler │    │Scheduler │
              │  "Main"  │    │  "AI"    │    │ "Physics"│
              └──────────┘    └──────────┘    └──────────┘
                    │                 │                 │
                    └─────────────────┼─────────────────┘
                                      │
                              ┌───────────────┐
                              │    SqHost     │
                              │ (your .NET app)│
                              └───────────────┘
```

## Decisions

| Decision | Choice |
|---|---|
| Scope | Core language + extensible host API |
| Compatibility | Modernized dialect (not drop-in SQF compatible) |
| Execution | Stack-based bytecode VM |
| Scheduling | Cooperative fibers + multi-thread schedulers |
| Integration | CLI tool + embeddable library |
| .NET Target | .NET 10 |
| Serialization | Binary `.sqfc` format |
| Unity | Design for it, build later |

## Sources

Design aligned with official Bohemia Interactive documentation:
- [SQF Syntax](https://community.bistudio.com/wiki/SQF_Syntax)
- [Order of Precedence](https://community.bistudio.com/wiki/Order_of_Precedence)
- [Control Structures](https://community.bistudio.com/wiki/Control_Structures)
- [Scheduler](https://community.bistudio.com/wiki/Scheduler)
- And 20+ additional wiki pages (see [plan](docs/plan.md) for full list)

## License

TBD

## Roadmap

| Version | Focus | Key Items |
|---|---|---|
| **v0.7** ✅ | Language complete | Full language, scheduler, 100+ commands, CLI, thread safety |
| **v0.8** | Compiler & runtime | Dead code elimination, preprocessor, range `..`, fiber priority/timeout |
| **v0.9** | Tooling & polish | DAP debugger, VS Code extension, `--watch`, string interpolation, `callExtension` |
| **v1.0** | Stable | NuGet packages, API docs, benchmarks, compatibility guarantee |

See [Scope & Roadmap](docs/scope-and-roadmap.md) for full details.

## AI-Assisted Development

This project was created with AI-assisted coding tools. All architecture, design
decisions, and feature direction were made under competent human supervision by
a skilled SQF scripter and professional C# programmer. AI tools were used to
accelerate implementation, but every line of code was reviewed, tested, and
validated by a human.

That said, errors and bugs may still be present. If you encounter issues at
compile time or runtime, please report them. Contributions and fixes are welcome.

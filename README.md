# SQ# (SQF Sharp)

**Bring SQF scripting to any .NET project.**

SQ# is a modernized, embeddable reimplementation of Arma 3's SQF scripting language
for .NET 10. It brings the familiar SQF syntax, operator-based design, and cooperative
scheduling model to any .NET application — game engines, tools, servers, or CLI apps.

## Status

� **Alpha** — Lexer, parser, compiler, VM, and CLI working. Scheduler and StdLib in progress.

## Quick Example

```sqf
// SQ# script — familiar SQF syntax, modern enhancements
private _units = allUnits select { alive _x && side _x == west };
private _count = count _units;

systemChat f"Found {_count} friendly units";

// Async with promises (array-on-right: [scheduler, code])
private _handle = spawnOn ["AI", {
    private _result = heavyPathfinding(_this);
    return _result;
}];

// Await with timeout (array form for multi-params)
private _path = await [_handle, 5];
```

## Key Features

- **SQF-compatible syntax** — familiar `call`/`spawn`/`execVM`, code-as-data, operator precedence
- **Modern enhancements** — string interpolation, type annotations, try/catch, module imports
- **Bytecode VM** — stack-based, fast execution, binary `.sqfc` serialization
- **Hybrid scheduling** — cooperative fibers with time budget, multi-scheduler, real threading
- **Implicit thread safety** — scheduler-local globals, ownership tracking, freeze/channel/shared primitives
- **Extensible host API** — register custom commands by arity, precedence, and thread safety level
- **Promise system** — Script Handles as async/await promises with combinators (All/Race/Any)
- **NuGet packages** — embed in any .NET app, CLI tool for running scripts standalone

## Documentation

| Document | For | Description |
|---|---|---|
| [SQ# Enhancements](docs/sqsharp-enhancements.md) | 👥 Everyone | **Master list** — everything SQ# brings over SQF |
| [Scope & Roadmap](docs/scope-and-roadmap.md) | 👥 Everyone | What SQ# owns, what the host owns, what's coming |
| [SQ# for SQF Scripters](docs/for-sqf-scripters.md) | 🎮 SQF users | Migration guide — what changed, how to script |
| [SQ# for .NET Developers](docs/for-dotnet-devs.md) | 💻 .NET devs | Embedding guide, NuGet, host API |
| [Quick Reference](docs/quick-reference.md) | 📋 Everyone | One-page language reference card |
| [Multithreading](docs/multithreading.md) | 🎮 SQF users | Fibers, schedulers, channels, shared, freeze/thaw |
| [Multiplayer](docs/multiplayer.md) | 🎮 SQF users | Locality, remoteExec, publicVariable, JIP, patterns |
| [Code Optimisation](docs/optimisation.md) | 🎮 SQF users | Performance tips, do/don't, scheduling |
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
│   ├── SQSharp.Core/            # SqValue, SqType, core abstractions
│   ├── SQSharp.Language/        # Lexer, Pratt Parser, AST
│   ├── SQSharp.Compiler/        # AST → IR → Bytecode
│   ├── SQSharp.VM/              # Stack VM
│   ├── SQSharp.Scheduler/       # Fiber engine, cooperative scheduling
│   ├── SQSharp.Host/            # Extensible host API
│   ├── SQSharp.StdLib/          # Standard library commands
│   ├── SQSharp.CLI/             # dotnet tool
│   └── SQSharp.Preprocessor/    # Legacy preprocessor (opt-in)
├── tests/
├── samples/
└── docs/
```

## Decisions

| Decision | Choice |
|---|---|
| Scope | Core language + extensible host API |
| Compatibility | Modernized dialect (not drop-in SQF compatible) |
| Execution | Stack-based bytecode VM |
| Scheduling | Hybrid: cooperative fibers + multi-thread schedulers |
| Integration | CLI tool + NuGet packages |
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

## Milestones

- **M1**: Full language + host demo (lexer, parser, compiler, VM, scheduler, 50+ commands)
- **M2**: Polish + tooling (.sqfc serialization, legacy preprocessor, DAP debugger, VS Code extension)
- **M3**: Ecosystem (Unity package, docs site, project templates)

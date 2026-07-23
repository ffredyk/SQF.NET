---
description: "Use when working on SQ# (SQF Sharp) — the .NET reimplementation of Arma 3's SQF scripting language. Covers project structure, architecture decisions, conventions, scope boundaries, known issues, and testing patterns."
applyTo: "**"
---
# SQ# Project Context

## What SQ# Is

SQ# is a modernized, embeddable reimplementation of Arma 3's SQF scripting language
for .NET 10. It brings SQF syntax, operator-based design, and cooperative scheduling
to any .NET application — game engines, tools, servers, CLI apps.

**Key principle**: SQ# is a scripting language + runtime. Host owns the world.
Networking, objects, UI, physics — host responsibility. See `docs/scope-and-roadmap.md`.

## Project Structure (9 Projects)

```
src/SQSharp.Core/       SqValue, SqType, SqArray, SqHashMap, SqCode, SqSharedValue,
                        SqError types, SqFormat, BytecodeChunk, Instruction,
                        OpCode, SqBinarySerializer
src/SQSharp.Language/   Lexer (tokens), Pratt Parser (AST), Token types
src/SQSharp.Compiler/   AST → Bytecode (single-pass recursive walk)
src/SQSharp.VM/         Stack VM (29 opcodes), ISqScheduler interface, builtin commands
src/SQSharp.Scheduler/  SqFiber (lightweight execution context),
                        SqScheduler (cooperative, time-budgeted), ScriptHandle
src/SQSharp.Host/       SqHost — central host API, command registration,
                        save/load state, scheduler management
src/SQSharp.StdLib/     Standard library commands (work in progress)
src/SQSharp.CLI/        CLI: lex, parse, compile, run, repl, serialize, deserialize
src/SQSharp.Preprocessor/  Legacy SQF preprocessor (opt-in)
```

## Key Architecture Decisions

### Type System
- `SqValue` is a readonly struct with tagged union (NO explicit layout — GC issues)
- Types: Nothing, Boolean, Number, String, Array, Code, HashMap, Namespace,
  ScriptHandle, Error, FrozenArray, Channel, Shared, Scheduler, HostTypeBase+128
- `nil` is a storable value (does NOT delete variables — unlike SQF)
- Undefined variables throw `SqUndefinedVariableError`

### Parser (Pratt Parser)
- 11 precedence levels: PrecNone(0) → PrecUnary(11)
- PrecCall(9) for call/spawn, PrecHash(10) for #, PrecUnary(11) for prefix ops
- Identifiers treated as binary operators at PrecBinary(4) unless expression breaker
- Expression breakers: if, while, for, switch, import, try, return, global,
  private, shared, then, else, do, from, to, step, catch
- Unary greediness: non-local identifier + next token starts expr → UnaryCall
- `_x + 3` is Binary, `count _arr` is Unary, `player` is Nular

### Compiler
- Single-pass AST → Bytecode
- `CompileBody()` unwraps CodeLiteralNode (for if/while/try bodies)
- `CompileBinary` handles short-circuit `&&`/`||` with code blocks
- `CompileUnary` detects `throw` keyword → emits Throw opcode directly
- Implicit `Ret` added at end of compilation

### VM (Stack-Based)
- 29 opcodes: PushConst through TryEnd + MakeShared
- Cooperative: fibers yield via `_state = VmState.Yielded`
- Exception handler stack: `_handlerStack` for TryBegin/TryEnd/Throw
- `MakeError()` creates SqError with source position from debug info
- **Critical bug fixed**: ternary `argCount > 0 ? Pop() : null` causes
  implicit string→SqValue conversion crash. Use if-statement instead.

### Scheduler
- Cooperative fibers with 3ms time budget per Tick()
- Multiple schedulers per host, each with own global namespace
- `spawnOn` for cross-scheduler spawning
- Thread safety: ownership tracking, freeze/thaw, Channels (SPSC), shared (CAS)

### Scope Boundaries
- ✅ SQ# owns: lexer, parser, compiler, VM, scheduler, thread safety, StdLib, CLI
- 🏠 Host owns: networking (UDP/TCP), game engine integration, UI system,
  object system (vehicles, units), physics, rendering, file I/O

## Conventions

### C# Naming
- Private fields: `_camelCase` (e.g., `_chunk`, `_schedulerId`)
- Public methods/properties: PascalCase
- Local variables: camelCase
- No `var` preference — explicit types OK

### SQ# Script Syntax
- Variables: `_localVar` (local), `globalVar` (global)
- Commands: nular `player`, unary `hint "hello"`, binary `_arr pushBack 5`
- `call` inherits scheduler, `spawn` creates fiber, `spawnOn` targets scheduler
- `shared _x = 0` declares CAS-based atomic variable (like `private` but atomic)
- `freeze`/`thaw` for immutable sharing, `Channel` for message passing

### Error Handling
- Errors reported as: `<script>(line,col): ErrorType: message`
- `SqTypeError`, `SqUndefinedVariableError`, `SqOwnershipError`, `SqThreadSafetyError`
- UnaryCall/BinaryCall catch exceptions, convert to error values with position
- Zero divisor detection in `/` and `%`

### Command Registration
- VM builtins (RegisterBuiltinCommands): math ops, array ops, scheduler queries,
  shared ops, throw, params stub
- Host core (RegisterCoreCommands): arithmetic with Shared unwrap, comparison,
  string ops, math commands, compile, createHashMap, execVM
- Host arma compat (DeclareArmaCompatCommands): hint, systemChat, diag_log
- Host scheduler (DeclareSchedulerCommands): allSchedulers, schedulerName, stats,
  sendTo
- Host multiplayer (DeclareMultiplayerCommands): isServer, remoteExec, publicVariable,
  player placeholder, owner placeholder

### Common Pitfalls
1. **Ternary with Pop() crashes**: `argCount > 0 ? Pop() : null` → use if-statement
2. **Command name collisions**: Host registers "set" twice → merged with type dispatch
3. **call/spawn in infix**: Not special-parsed (BinaryCall path returns nil).
   Prefix `call {code}` works via ParseCallSpawn.
4. **Unary minus**: `-` as unary needs registration. `abs (-42)` needs parens.
5. **`scheduler` vs `owner`**: `scheduler` = local ownership, `owner` = hardware machine.
   `scheduler` returns -1 for non-local args.
6. **Implicit Ret**: Compile() adds Ret at end. Child chunks get their own Ret.

## Current State (v0.2+)

### ✅ Working
- Lexer, Pratt parser (20+ AST nodes), bytecode compiler
- Stack VM (29 opcodes), cooperative fiber scheduler
- Multi-scheduler with thread safety (ownership, freeze/thaw, channels, shared)
- try/catch/throw with full error propagation
- Lazy `&&`/`||` with code block short-circuit
- Math commands: sin, cos, tan, asin, acos, sqrt, abs, exp, log, floor, ceil,
  round, atan2, pow, random
- createHashMap with set/get (binary get dispatches by type)
- Binary serialization: SqBinarySerializer + host SaveState/LoadState
- CLI: lex, parse, compile, run, repl, serialize, deserialize
- Error diagnostics: file(line,col): ErrorType: message
- Scheduler commands: currentScheduler, clientOwner, allSchedulers,
  schedulerName, schedulerStats, sendTo
- 96 tests passing, 3 skipped

### ⚠️ Known Gaps
- `call {code}` / `spawn {code}` → MakeCode path now FIXED (ternary→if)
- `switch`/`case` → parser stubbed, needs AST redesign
- `params` with defaults → needs compiler integration
- `execVM` registered but untested for cross-file execution
- Preprocessor (`SQSharp.Preprocessor`) exists but CLI not wired
- No DAP debugger, no VS Code extension
- `player`/`allPlayers`/`didJIP`/`owner` are host placeholders

### 🔜 Planned (v0.3+)
- Constant folding, dead code elimination
- Fiber cancellation (`terminate`), fiber priority
- `.sqfc` binary serialization CLI
- `--watch` mode, VS Code extension

## Testing

```bash
dotnet test                                    # 96 pass, 3 skip
dotnet run --project src/SQSharp.CLI -- run file.sqf  # Execute script
dotnet run --project src/SQSharp.CLI -- lex file.sqf  # Token dump
dotnet run --project src/SQSharp.CLI -- parse file.sqf # AST dump
```

Test projects: SQSharp.Core.Tests, SQSharp.Language.Tests,
SQSharp.Compiler.Tests, SQSharp.VM.Tests

## Key Files

| File | Purpose |
|---|---|
| `docs/scope-and-roadmap.md` | What SQ# owns vs host |
| `docs/sqsharp-enhancements.md` | All SQ# advantages over SQF |
| `docs/multithreading.md` | Full threading guide |
| `docs/multiplayer.md` | Full MP guide |
| `src/SQSharp.Core/SqValue.cs` | Universal value type |
| `src/SQSharp.Language/Parser.cs` | Pratt parser with precedence |
| `src/SQSharp.VM/SqVm.cs` | Stack VM + builtins |
| `src/SQSharp.Host/SqHost.cs` | Host API + command registration |
| `src/SQSharp.Scheduler/SqScheduler.cs` | Cooperative scheduler |

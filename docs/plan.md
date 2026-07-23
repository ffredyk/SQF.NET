# SQ# Implementation Plan

> Full architectural plan for the SQ# (SQF Sharp) scripting engine.
> See README.md for project overview.

## Decisions

| Decision | Choice |
|---|---|
| **Scope** | Core language + extensible host API (no Arma commands built-in) |
| **Compatibility** | Modernized SQF dialect (not drop-in compatible) |
| **Execution** | Stack-based bytecode VM |
| **Scheduling** | Hybrid — cooperative per scheduler, multi-scheduler on different threads |
| **Integration** | Standalone CLI + NuGet library |
| **.NET** | .NET 10 |
| **Name** | SQ# (SQF Sharp) |
| **Preprocessor** | Hybrid — module system (default) + optional preprocessor pass for legacy .sqf |
| **Serialization** | Binary .sqfc bytecode format |
| **First milestone** | Full language + host demo |
| **Unity** | Design for it, build later |

## Architecture Pipeline

```
Source (.sqf) → [Legacy Preprocessor opt-in] → Lexer → Tokens → Pratt Parser → AST
    → Semantic Analyzer → IR → Bytecode Compiler → Bytecode → VM → Scheduler → Host API
```

## Project Structure

```
SQF.NET/
├── src/
│   ├── SQSharp.Core/            # SqValue, SqType, core abstractions
│   ├── SQSharp.Language/        # Lexer, Pratt Parser, AST definitions
│   ├── SQSharp.Compiler/        # AST → IR → Bytecode compiler
│   ├── SQSharp.VM/              # Stack VM, instruction dispatch
│   ├── SQSharp.Scheduler/       # Fiber engine, cooperative scheduling
│   ├── SQSharp.Host/            # IHost, command registration by arity+precedence
│   ├── SQSharp.StdLib/          # Standard commands (math, string, array, logic)
│   ├── SQSharp.CLI/             # dotnet sqf run/repl/compile/serve
│   └── SQSharp.Preprocessor/    # Opt-in legacy #define + comment stripping
├── tests/
│   ├── SQSharp.Core.Tests/
│   ├── SQSharp.Language.Tests/  # Parser/precedence tests
│   ├── SQSharp.Compiler.Tests/
│   ├── SQSharp.VM.Tests/
│   └── SQSharp.Integration.Tests/
├── samples/
│   ├── HostMinimal/
│   └── HostGame/
└── docs/
    ├── plan.md                  # This file
    ├── language-spec.md         # Syntax, operators, precedence, control structures
    ├── types.md                 # Data types, magic types, nil/void
    ├── scheduler-threads.md     # Scheduler model, thread safety
    ├── promises.md              # Script handle, promise system
    └── arrays.md                # Array semantics
```

## NuGet Packages

| Package | Role |
|---|---|
| `SQSharp.Core` | SqValue, type system, zero deps |
| `SQSharp.Runtime` | Core + VM + scheduler |
| `SQSharp.Compiler` | Lexer + Pratt parser + bytecode compiler |
| `SQSharp.CLI` | dotnet tool (run, repl, compile) |
| `SQSharp.Hosting` | Host abstractions + std library commands |
| `SQSharp.Unity` | Unity MonoBehaviour host, coroutine bridge (future) |

## Host API

```csharp
public interface ISqHost
{
    // Register commands by arity + precedence for disambiguation
    void RegisterNular(string name, Func<SqValue> handler);
    void RegisterUnary(string name, Func<SqValue, SqValue> handler);
    void RegisterBinary(string name, Func<SqValue, SqValue, SqValue> handler,
        int precedence, ThreadSafety threadSafety = ThreadSafety.Isolated);

    // Register a type that can be returned by commands
    void RegisterType<T>(string typeName) where T : class;

    // Resolve unknown names (enables dynamic/late-bound commands)
    SqValue? ResolveNular(string name);
    SqValue? ResolveUnary(string name, SqValue arg);
    SqValue? ResolveBinary(string name, SqValue left, SqValue right);

    // Scheduler management
    ISqScheduler MainScheduler { get; }
    ISqScheduler CreateScheduler(string name, double timeBudgetMs = 3.0);

    // Lifecycle callbacks
    void OnScriptStart(SqFiber fiber);
    void OnScriptEnd(SqFiber fiber, SqValue? result);
    void OnError(SqFiber fiber, SqError error);
    void OnPrint(string message, SqPrintChannel channel);

    // Time (for sleep, diag_tickTime)
    double CurrentTime { get; }
}
```

## Milestones

### M1 — Full Language + Host Demo
- Lexer + Pratt parser with full precedence table (levels 1-11)
- Control structure desugaring (if/while/for/switch)
- Semantic analyzer (scoping, basic type check)
- Bytecode compiler (all constructs, arity-dispatched commands)
- Stack VM executing bytecode
- Cooperative fiber scheduler (single-thread first)
- Host API with arity+precedence registration
- 50+ standard commands (math, string, array, logic, control flow)
- Sample game-like host demo
- CLI: `sqf run`, `sqf repl`, `sqf compile`

### M2 — Polish + Tooling
- Binary `.sqfc` bytecode serialization
- Opt-in legacy preprocessor pass
- Multi-thread schedulers + thread safety enforcement
- Debug Adapter Protocol support
- VS Code extension (syntax highlighting, debugging)
- Performance benchmarks + VM optimization (inlining, superinstructions)

### M3 — Ecosystem
- Unity integration package
- Documentation site with language spec
- Project templates (classlib, console, unity)
- Community samples repository

## Detail Documents

For in-depth specification of each subsystem, see:

- **[language-spec.md](language-spec.md)** — Tokens, operator precedence table, control structures, parser design, bytecode VM instructions
- **[types.md](types.md)** — SqValue, data types, magic types, nil/Nothing/Void, HashMapKey, magic variables
- **[scheduler-threads.md](scheduler-threads.md)** — Scheduled/unscheduled environments, call/spawn/execVM, scheduler design, implicit thread safety
- **[promises.md](promises.md)** — Script handles as promises, async/await, combinators, C# interop
- **[arrays.md](arrays.md)** — Array reference semantics, index quirks, mutation vs copy, SQ# fixes

## Wiki Sources

Design aligned with Bohemia Interactive community wiki:
- SQF Syntax, Order of Precedence, Operators, Control Structures, Data Type
- Anything, Nothing, Void, HashMapKey
- For Type, If Type, While Type, Switch Type, With Type
- Array, Script Handle, Code, String, Namespace
- call, spawn, execVM, compile, compileScript
- Scheduler, params, Magic Variables, private, isEqualTo
- PreProcessor Commands

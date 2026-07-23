# Language Specification

## Fundamental Design

SQ# is an **operator-based language**. Like SQF, it has barely any language structures —
everything is provided via operators (scripting commands). **Including control flow.**
`if`, `while`, `for`, `switch` are just operators that chain via **Helper Types**.

## Operator Arities

| Type | Signature | Example | Behavior |
|------|-----------|---------|----------|
| **Nular** | `operator` — no args | `allUnits`, `player`, `nil` | Returns computed state each call. NOT a cached variable. |
| **Unary** | `operator <right>` | `count _arr`, `str 123` | Greedy: consumes immediate right-side value. |
| **Binary** | `<left> operator <right>` | `_a + _b`, `_arr select 0` | Resolved by precedence; equal→left-to-right. |

## Syntax Basics

- **Terminators**: `;` (preferred) or `,`
- **Comments**: `//` line, `/* */` block (removed during preprocessing)
- **Assignment**: Only `=`. Arrays by reference. `+_arr` for deep copy.
- **Brackets**: `()` precedence, `[]` arrays, `{}` code blocks
- **Whitespace**: Ignored. "Line" begins at first non-whitespace.

## Full Operator Precedence Table

Higher number = higher priority. Equal → left-to-right.

| Precedence | Category | Operators |
|:---:|---|---|
| **11** | Nular, values, brackets | variables, literals, `()`, `[]`, `{}`, `""`, `''` |
| **10** | Unary operators | `+a`, `-a`, `!a`, `not a`, `count _arr`, `str _val` |
| **9** | Hash-select | `array # index` |
| **8** | Power | `a ^ b` |
| **7** | Mul/Div/Rem, atan2 | `*`, `/`, `%`, `mod`, `atan2` |
| **6** | Add/Sub, min, max | `+`, `-`, `min`, `max` |
| **5** | else | `else` (if-then-else chaining) |
| **4** | Binary commands | `select`, `set`, `resize`, switch `:`, `setDir` |
| **3** | Comparisons | `==`, `!=`, `>`, `<`, `>=`, `<=` |
| **2** | Logical AND | `&&`, `and` |
| **1** | Logical OR | `\|\|`, `or` |

### Key Precedence Rules

1. **Unary (10) > Binary (4)**: `count _arr select 2` = `(count _arr) select 2`
2. **Arithmetic (7,6) > Binary cmds (4)**: `a + b select 0` = `(a + b) select 0`
3. **Comparisons (3) > Logic (2,1)**: `a == b && c == d` = `(a == b) && (c == d)`
4. **else at 5**: between add/sub (6) and binary cmds (4)
5. **select at 4**: lower than arithmetic, higher than comparisons

## Control Structures = Operators + Helper Types

Control structures are NOT special syntax. They're normal operators that use
**helper types** to chain together. SQ# provides clean syntax sugar that desugars to operator chains.

### if-then-else

```
// Sugar form (SQ#):
if (condition) { code1 } else { code2 }

// Desugars to operator chain:
if(condition) .then({code1}) .else({code2})

// Raw operator form also valid:
if (condition) then { code1 } else { code2 }
```

Internally:
1. `if` — unary (prec 10). Returns **If Type**.
2. `then` — binary. Takes If Type + Code → evaluates.
3. `else` — binary (prec 5). Packs [Code, Code] → feeds to then.

### while-do

```
while { condition } do { body };
```

1. `while` — unary. Returns **While Type**.
2. `do` — binary. Takes While Type + Body Code → executes loop.

### for (Two Forms)

**Array form:**
```
for [{INIT}, {CONDITION}, {STEP}] do { BODY };
```

**From-to-step form:**
```
for "VARNAME" from START to END step STEP do { BODY };
```

Each word chains For Type: `for` → `from` → `to` → `step` → `do`.

### switch-do-case

```
switch (VARIABLE) do {
    case VALUE1: { CODE1 };
    case VALUE2: { CODE2 };
    default { CODE3 };
};
```

1. `switch` — unary. Returns **Switch Type**.
2. `do` — binary. Switch Type + Code.
3. `case VALUE:` — colon at prec 4.
4. `default` — nular inside switch context.

## Parser Design (Pratt / Precedence Climbing)

### Token Types
```
IDENTIFIER, NUMBER, STRING, CODE_BLOCK
PLUS, MINUS, STAR, SLASH, PERCENT, CARET
EQ, NEQ, LT, GT, LTE, GTE
AND, OR, NOT, BANG
HASH, COLON
SEMICOLON, COMMA
LPAREN, RPAREN, LBRACKET, RBRACKET
ASSIGN
```

### Pratt Binding Powers (Precedence → Binding)

```csharp
static class Precedence
{
    public const int None = 0;
    public const int LogicalOr = 1;      // ||, or
    public const int LogicalAnd = 2;     // &&, and
    public const int Comparison = 3;     // ==, !=, <, >, <=, >=
    public const int BinaryCommand = 4;  // select, set, switch :
    public const int Else = 5;           // else
    public const int AddSub = 6;         // +, -, min, max
    public const int MulDiv = 7;         // *, /, %, mod, atan2
    public const int Power = 8;          // ^
    public const int HashSelect = 9;     // #
    public const int Unary = 10;         // prefix +, -, !, not
    public const int Nular = 11;         // vars, literals, brackets
}
```

### Parse Flow

1. **Prefix**: parse nular (variable, literal, brackets) or unary operator
2. **Infix**: while next token prec > current prec → parse as binary
3. **Desugaring**: control flow patterns lowered to operator chains

## Bytecode VM Instructions

### Value Operations
```
PUSH_CONST <idx>         ; push constant from pool
PUSH_LOCAL <idx>         ; push local var
STORE_LOCAL <idx>        ; pop → store local
PUSH_GLOBAL <nameId>     ; push global
STORE_GLOBAL <nameId>    ; pop → store global
```

### Arity-Dispatched Commands
```
NULAR_CALL <cmdId>       ; call nular → push result
UNARY_CALL <cmdId>       ; pop 1 arg → call → push
BINARY_CALL <cmdId>      ; pop right, pop left → call → push
```

### Code & Arrays
```
MAKE_CODE <addr>         ; push code reference
MAKE_ARRAY <count>       ; pop count items → push array
MAKE_HASHMAP             ; create empty hashmap
```

### Control Flow
```
JUMP <offset>            ; unconditional branch
JUMP_IF_FALSE <offset>   ; pop, jump if false
JUMP_IF_TRUE <offset>    ; pop, jump if true
CALL <argc>              ; call code value
SPAWN <argc>             ; spawn new fiber
RET                      ; return from call
YIELD                    ; yield fiber to scheduler
```

### Stack
```
DUP, POP, SWAP
```

### Compiler Mapping
```
a + b       → PUSH_LOCAL a, PUSH_LOCAL b, BINARY_CALL <add>
!cond       → PUSH_LOCAL cond, UNARY_CALL <not>
arr select 0 → PUSH_LOCAL arr, PUSH_CONST 0, BINARY_CALL <select>
_a = expr   → [expr bytecode], STORE_LOCAL <_a>, DUP
```

## Operator Semantics

### Arithmetic
| Op | Arity | Prec | Signature |
|----|-------|------|-----------|
| `+` | Unary | 10 | `Number → Number` (identity) |
| `-` | Unary | 10 | `Number → Number` (negation) |
| `+` | Binary | 6 | `Number×Number → Number` |
| `-` | Binary | 6 | `Number×Number → Number` |
| `*` | Binary | 7 | `Number×Number → Number` |
| `/` | Binary | 7 | `Number×Number → Number` |
| `%`, `mod` | Binary | 7 | `Number×Number → Number` |
| `^` | Binary | 8 | `Number×Number → Number` |

### Array
| Op | Arity | Prec | Signature |
|----|-------|------|-----------|
| `+` | Unary | 10 | `Array → Array` (deep copy) |
| `+` | Binary | 6 | `Array×Array → Array` (concat) |
| `-` | Binary | 6 | `Array×Array → Array` (remove all) |

### String
| Op | Arity | Prec | Signature |
|----|-------|------|-----------|
| `+` | Binary | 6 | `String×String → String` |

### Logical
| Op | Arity | Prec | Signature |
|----|-------|------|-----------|
| `!`, `not` | Unary | 10 | `Boolean → Boolean` |
| `&&`, `and` | Binary | 2 | `Boolean×Boolean → Boolean` |
| `\|\|`, `or` | Binary | 1 | `Boolean×Boolean → Boolean` |

### Comparison
| Op | Arity | Prec | Signature |
|----|-------|------|-----------|
| `==`, `!=` | Binary | 3 | `Any×Any → Boolean` |
| `<`, `>`, `<=`, `>=` | Binary | 3 | `Number×Number → Boolean` |

## SQ# Language Features

### Keep from SQF
- Operator-first design with nular/unary/binary model
- Control structures as operators chained by helper types
- `call` (inherits env) / `spawn` (always scheduled) / `execVM`
- `compile`, `compileScript`, `loadFile`, `preprocessFileLineNumbers`
- Code-as-data (`{}` blocks are first-class values)
- Full precedence table (levels 11→1)
- `forEach`, `select`, `apply`, `findIf`, `arrayIntersect`
- `params`/`param` with type/size validation
- `private`, `nil`, magic variables, namespaces
- `;` terminator, legacy preprocessor (opt-in)

### Add (SQ# Modernizations)
- Clean syntax sugar for control structures
- Optional static type annotations
- Module/import system
- String interpolation, escape sequences, verbatim/multi-line strings
- `try`/`catch`, structured errors
- `==` is strict (`isEqualTo` semantics)
- `nil` is storable (not deletive), `undefine` for deletion
- `await`, promise combinators, `spawnOn`/`spawnParallel`
- Implicit thread safety, `Freeze`/`Channel`/`Shared`
- Regular expressions, `#pragma` directives
- Debugger hooks

### Drop
- `#define`/`#ifdef` (opt-in legacy only)
- `config.cpp` syntax
- Arma-specific types (unless host registers)
- Legacy `comment` operator
- SQS syntax
- nil-deletes-variable behavior
- Single-quote preprocessor parsing
- `spawn` non-deterministic order
- Nested `#ifdef` limitation
- `==` type coercion
- Banker's rounding for array indices

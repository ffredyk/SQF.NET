// ============================================================
// basics.sqf — Variables, Literals, Types, Operators
// SQ# Language Sample
// ============================================================

// ---- Literals ----
private _num = 42;               // Number (IEEE 754 double)
private _pi = 3.14159;
private _big = 2.5e10;           // Scientific notation
private _neg = -17;
private _hex = 0xFF;             // Hex literal (SQ# addition)

private _flag = true;            // Boolean
private _off = false;

private _name = "hello";         // Double-quoted string
private _code = 'SQ#';           // Single-quoted string (same as double)
private _nil = nil;              // Variable DELETED (nil assignment = SQF behavior)
// Unlike old SQ# docs: nil is NOT storable — it deletes the variable.

// ---- Verbatim & Multi-line strings (SQ# additions) ----
private _path = @"C:\Users\Name\Documents";   // No escape processing
private _block = """
    Line one
    Line two
    Line three
    """;                                      // Multi-line

// ---- Variables ----
private _local = 5;              // Local (always starts with _)
global CONFIG_VERSION = "1.0";   // Explicit global — no implicit leak

// Type annotations (optional, SQ# addition)
private _count: int = 0;
private _label: string = "default";
private _tags: string[] = [];

// ---- Assignment ----
// Only = operator. No += -= *= /=.
// Assignment IS an expression — returns assigned value.
private _x = 10;
private _y = (_x = _x + 5);     // _x = 15, _y = 15

// ---- Arithmetic Operators ----
private _sum = 10 + 5;           // 15   (prec 6)
private _diff = 10 - 5;          // 5    (prec 6)
private _prod = 10 * 5;          // 50   (prec 7)
private _quot = 10 / 5;          // 2    (prec 7)
private _mod = 10 % 3;           // 1    (prec 7)
private _power = 2 ^ 8;          // 256  (prec 8)
private _negate = -_x;           // Unary negation (prec 10)

// Precedence: MulDiv > AddSub
private _result = 1 + 2 * 3;     // 7 (not 9!)
private _grouped = (1 + 2) * 3;  // 9

// ---- Comparison Operators (prec 3, strict like isEqualTo) ----
private _eq = (5 == 5);          // true — case-sensitive, deep comparison
private _neq = (5 != 3);         // true
private _lt = (3 < 5);           // true
private _gt = (10 > 7);          // true
private _lte = (5 <= 5);         // true
private _gte = (8 >= 2);         // true

// Strict equality (SQ# == acts like isEqualTo):
private _strict1 = ("hello" == "HELLO");   // false (case-sensitive)
private _strict2 = ([1,2] == [1,2]);       // true (deep compare works!)
private _strict3 = (nil == nil);           // true
private _strict4 = (nil == 42);            // false

// ---- Logical Operators ----
// && (and) prec 2, || (or) prec 1
private _and = (true && false);           // false
private _or = (true || false);            // true
private _not = !true;                      // false (unary, prec 10)
private _notAlt = not false;               // true (alias)

// Compound: comparisons > logic
private _check = (_x > 0 && _x < 100);   // (x > 0) && (x < 100)

// ---- Min/Max (prec 6, same as AddSub) ----
private _minVal = 5 min 10;     // 5
private _maxVal = 5 max 10;     // 10

// ---- Type checking ----
// isNil checks if variable is undefined (SQF behavior):
private _myVar = 42;
private _isNil1 = isNil _myVar;          // false (variable exists)
_myVar = nil;                            // Variable DELETED (SQF semantics)
private _isNil2 = isNil _myVar;          // true (variable was deleted)
// isNil also works on values directly (e.g., for array elements):
private _arr = [1, nil, 3];
private _isNil3 = isNil (_arr select 1); // true (array element is nil)
private _typeStr = typeName _num;        // "NUMBER" (string)

// ---- print / systemChat / hint ----
print "Basics sample loaded!";   // Always available (→ host.OnPrint)
systemChat "Hello from SQ#";     // Arma compat (host must opt-in)
hint "Ready.";                   // Arma compat (aliases print)

// ---- Return last expression ----
"basics complete"

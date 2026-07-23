// ============================================================
// error-handling.sqf — try/catch, Error types, validation
// SQ# Language Sample
// ============================================================

// ---- try/catch — Basic error handling ----
try {
    private _result = 10 / 0;            // Division by zero
} catch {
    // _exception contains the error
    print f"Caught error: {_exception}";
};

// ---- try/catch with typed error ----
try {
    private _arr = [1, 2, 3];
    // This would fail if index were computed OOB:
    private _val = _arr select 5;
    print f"Value: {_val}";              // nil in SQ# (safe OOB)
} catch (_error) {
    print f"Error: {_error}";
};

// ---- Catch specific error types (SQ# addition) ----
// try {
//     private _code = compile "invalid syntax @@@";
// } catch (_error: CompileError) {
//     print f"Compile error at line {_error.line}: {_error.message}";
// } catch (_error: RuntimeError) {
//     print f"Runtime error: {_error.message}";
// };

// ---- throw — Raise custom errors ----
private _validateAge = {
    params ["_age"];
    if (_age < 0) throw "Age cannot be negative";
    if (_age > 150) throw "Age unrealistic";
    _age  // return valid age
};

try {
    -5 call _validateAge;
} catch {
    print f"Validation failed: {_exception}";
};

// ---- Structured error objects (SQ# addition) ----
// _error provides richer info than _exception:
// _error.message  — error description string
// _error.type     — error type enum
// _error.stack    — call stack trace
// _error.source   — source file/line

// ---- Undefined variable detection (SQ# strict mode) ----
// SQF: silently crashes with confusing error
// SQ#: clear error message
// private _val = _undefinedVar;         // ERROR: Undefined variable '_undefinedVar'

// ---- Type errors are clear ----
try {
    private _x = { 1 + 2 };             // Code value
    private _y = _x + 3;                // Can't add Code + Number
} catch {
    print f"Type error: {_exception}";   // "Expected Number, got Code"
};

// ---- Nil safety ----
// SQ#: nil deletes variables (SQF-compatible behavior):
private _val = 42;
_val = nil;                           // Variable DELETED
private _isNilCheck = isNil _val;     // true (variable is gone)
// isNil also checks values directly:
private _arr = [1, nil, 3];
if (_arr select 1 == nil) then {
    print "Array element is nil";      // works — nil is still a value, just can't be stored in variables
};

// ---- Array safety (SQ# vs SQF) ----
private _arr = [1, 2, 3];
// In SQF:
//   _arr select -1   → Error Zero Divisor (confusing!)
//   _arr select 3    → nil (exactly at count)
//   _arr select 4    → Error Zero Divisor (inconsistent!)
// In SQ#:
//   _arr[-1] or _arr[99] → nil (safe, consistent)

// ---- Error propagation in promises ----
private _failingTask = spawn {
    sleep 1;
    throw "Task failed!";
};

// Errors captured and propagated through promise chain:
_failingTask continueWith {
    // _this IS the error if task threw
    if (_this isError) then {
        print f"Task error: {_error.message}";
    } else {
        print f"Task success: {_this}";
    };
};

// Or await and catch:
try {
    private _result = await _failingTask;  // throws
} catch {
    print f"Await failed: {_exception}";
};

// ---- Assertions (for debugging) ----
// assert condition — halts if false (debug mode):
private _x = 42;
// assert (_x > 0);                       // OK, passes
// assert (_x < 0);                       // HALTS in debug mode

// ---- Defensive coding patterns ----
// Safe division:
private _safeDivide = {
    params ["_a", "_b"];
    if (_b == 0) exitWith { nil };
    _a / _b
};

// Safe array access:
private _safeGet = {
    params ["_arr", "_idx", "_default"];
    if (_idx < 0) exitWith { _default };
    if (_idx >= count _arr) exitWith { _default };
    _arr select _idx
};

// Validation wrapper:
private _require = {
    params ["_value", "_condition", "_message"];
    if (!(_value call _condition)) throw _message;
    _value
};
// Usage: 42 call { _require [_this, { _this > 0 }, "Must be positive"]; };

"error-handling complete"

// ============================================================
// strings.sqf — String handling, interpolation, formatting
// SQ# Language Sample
// ============================================================

// ---- String Creation ----
private _name = "SQ# Script Engine";
private _single = 'hello world';     // Single-quote (same as double)
private _empty = "";

// ---- Escape Sequences (SQ# addition — SQF has none!) ----
private _newline = "Line 1\nLine 2";           // \n newline
private _tabbed = "Col1\tCol2\tCol3";           // \t tab
private _quoted = "He said \"Hello\"";          // \" escaped quote
private _path = "C:\\Users\\Name\\Documents";   // \\ backslash
private _unicode = "Smiley: \u263A";             // ☺ unicode escape

// ---- Verbatim String (SQ# addition) ----
// No escape processing — perfect for paths and regex:
private _winPath = @"C:\Users\Name\Documents";   // backslashes kept literal
private _regex = @"^\d{3}-\d{2}-\d{4}$";         // regex pattern

// ---- Multi-line String (SQ# addition) ----
private _multiline = """
    Roses are red,
    Violets are blue,
    SQ# is awesome,
    And so are you.
    """;

// ---- String Interpolation f"..." (SQ# addition) ----
private _playerName = "Foxhound";
private _hp = 75;
private _maxHp = 100;
private _ratio = _hp / _maxHp * 100;

// Compiled to format() call:
private _msg = f"Player {_playerName} has {_hp}/{_maxHp} HP ({_ratio}%)";
// Equivalent to: format ["Player %1 has %2/%3 HP (%4%)", _playerName, _hp, _maxHp, _ratio]

// Expressions inside interpolation:
private _x = 10;
private _y = 20;
private _math = f"{_x} + {_y} = {_x + _y}";   // "10 + 20 = 30"

// ---- format (SQF compat) ----
private _formatted = format ["Score: %1, Level: %2", 8500, 12];
// %1, %2, %3... positional placeholders

// ---- str — Any value to string ----
private _numStr = str 42;              // "42"
private _boolStr = str true;           // "true"
private _nilStr = str nil;             // "nil"
private _arrStr = str [1, 2, 3];       // "[1,2,3]"
private _codeStr = str { _x + 1 };     // "{_x + 1}"

// ---- Concatenation (only + operator) ----
private _greeting = "Hello, " + _playerName + "!";
// Note: each + creates a new string. For many concatenations, use joinString.

// ---- String Length & Access ----
private _text = "SQ# Rocks!";
private _len = count _text;            // 10 (character count)

// select — get character at index (returns single-char string):
private _firstChar = _text select 0;   // "S"
private _lastChar = _text select 9;    // "!"

// ---- Substring Search ----
private _haystack = "The quick brown fox";
private _contains = "brown" in _haystack;       // true
private _idx = _haystack find "quick";           // 4
private _notFound = _haystack find "absent";     // -1

// ---- Case Conversion ----
private _loud = toUpper "quiet";       // "QUIET"
private _soft = toLower "LOUD";        // "loud"

// ---- Trim ----
private _padded = "   hello   ";
private _clean = trim _padded;         // "hello"

// ---- splitString / joinString ----
private _csv = "alpha,bravo,charlie,delta";
private _items = _csv splitString ",";
// _items = ["alpha", "bravo", "charlie", "delta"]

private _tags = ["sqf", "script", "game"];
private _tagStr = _tags joinString " | ";
// "sqf | script | game"

// ---- toString / toArray (char code conversion) ----
private _codes = toArray "ABC";        // [65, 66, 67]
private _back = toString [72, 105];    // "Hi"

// ---- parseNumber ----
private _parsed = parseNumber "42.5";  // 42.5 (Number)
private _bad = parseNumber "hello";    // 0 (invalid → 0)

// ---- String comparison (strict, case-sensitive in SQ#) ----
private _same = ("hello" == "hello");         // true
private _caseDiff = ("Hello" == "hello");     // false (case-sensitive!)
private _order = ("abc" < "def");             // true (lexicographic)

// ---- String Building Optimization ----
// SLOW — each + creates a new string:
// _msg = "";
// for "_i" from 0 to 999 do { _msg = _msg + "x"; };

// FAST — build array, join once:
private _parts = [];
for "_i" from 0 to 999 do {
    _parts pushBack "x";
};
private _fastMsg = _parts joinString "";  // 1 allocation

// ---- Regular Expressions (SQ# addition) ----
// private _match = "abc123" =~ /[a-z]+\d+/;   // true
// private _extract = "abc123" =~ /(\d+)/;      // captures "123"

// ---- String in HashMaps ----
// Strings work as HashMap keys:
private _map = createHashMap;
_map set ["name", _playerName];
_map set ["hp", _hp];
private _stored = _map get "name";     // "Foxhound"

"strings complete"

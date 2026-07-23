// ============================================================
// data-structures.sqf — HashMaps, Namespaces, advanced types
// SQ# Language Sample
// ============================================================

// ============================================================
// HashMaps (Arma 3 2.02+ style)
// ============================================================

// ---- createHashMap — Create empty map ----
private _playerData = createHashMap;

// ---- set — Insert/update key-value ----
_playerData set ["name", "Foxhound"];
_playerData set ["rank", "Captain"];
_playerData set ["score", 8500];
_playerData set ["alive", true];
_playerData set ["position", [125.5, 3400.2, 0]];

// ---- get — Retrieve by key ----
private _name = _playerData get "name";          // "Foxhound"
private _rank = _playerData get "rank";          // "Captain"
private _score = _playerData get "score";         // 8500
private _missing = _playerData get "nonexist";    // nil

// get with default:
// private _def = _playerData getOrDefault ["nonexist", "unknown"];

// ---- Key types ----
// Valid keys: Number, Boolean, String, Code, Namespace, Side, NaN
// Arrays MUST be frozen to be keys:
private _map = createHashMap;
_map set [42, "number key"];               // Number key — OK
_map set [true, "boolean key"];             // Boolean key — OK
_map set ["str", "string key"];             // String key — OK

private _frozenKey = [1, 2, 3] freeze;
_map set [_frozenKey, "frozen array key"];  // Frozen Array key — OK

// ---- Check existence ----
private _hasName = "name" in _playerData;           // true
private _hasMissing = "nonexist" in _playerData;    // false

// ---- count — Number of entries ----
private _entryCount = count _playerData;

// ---- Delete entry ----
_playerData deleteAt "score";               // remove key
// _playerData get "score" → nil now

// ---- Iterate over HashMap (forEach) ----
_playerData set ["score", 8500];            // put back
{
    // _x = key, _y = value
    print f"  {_x} = {_y}";
} forEach _playerData;

// ---- keys / values — Get all keys or values as Array ----
// private _keys = keys _playerData;
// private _values = values _playerData;

// ---- createHashMapFromArray — From key-value pairs ----
private _configArr = [
    ["difficulty", "hard"],
    ["maxPlayers", 16],
    ["timeLimit", 30]
];
private _config = createHashMapFromArray _configArr;
private _difficulty = _config get "difficulty";  // "hard"

// ---- Nested HashMaps ----
private _world = createHashMap;
private _zone1 = createHashMap;
_zone1 set ["name", "Spawn Area"];
_zone1 set ["safe", true];

private _zone2 = createHashMap;
_zone2 set ["name", "Combat Zone"];
_zone2 set ["safe", false];
_zone2 set ["threat", 8];

_world set ["zone1", _zone1];
_world set ["zone2", _zone2];

// Access nested:
private _zone1Name = (_world get "zone1") get "name";  // "Spawn Area"

// ============================================================
// Namespaces — Named key-value stores
// ============================================================

// ---- missionNamespace — Global variables (scheduler-local in SQ#) ----
global WORLD_SEED = 12345;
global MAX_UNITS = 100;

// Access via namespace API:
missionNamespace setVariable ["SERVER_NAME", "My Server"];
private _serverName = missionNamespace getVariable "SERVER_NAME";  // "My Server"

// getVariable with default:
private _defaulted = missionNamespace getVariable ["MISSING", "fallback"];  // "fallback"

// ---- allVariables — List all variables in namespace ----
// private _allVars = allVariables missionNamespace;

// ---- with/do — Switch namespace context for a block ----
// with uiNamespace do {
//     myCtrlWidth = 200;    // sets in uiNamespace, not missionNamespace
// };

// ---- currentNamespace — Get current namespace ----
// private _ns = currentNamespace;

// ---- Custom namespaces ----
// Hosts can create additional namespaces:
// private _myNs = createNamespace "MyData";
// _myNs setVariable ["key", "value"];

// ============================================================
// Structured Data Patterns
// ============================================================

// ---- Pattern 1: HashMap as record/struct ----
private _makePlayer = {
    params ["_name", "_level"];
    private _p = createHashMap;
    _p set ["name", _name];
    _p set ["level", _level];
    _p set ["xp", 0];
    _p set ["inventory", []];
    _p
};

private _player1 = ["Foxhound", 5] call _makePlayer;
private _player2 = ["Shadow", 3] call _makePlayer;

// ---- Pattern 2: Array of HashMaps (table) ----
private _leaderboard = [];
_leaderboard pushBack (["Alice", 9500] call _makePlayer);
_leaderboard pushBack (["Bob", 7200] call _makePlayer);
_leaderboard pushBack (["Charlie", 8800] call _makePlayer);

// Sort by score descending:
// _leaderboard sort false;  // by first element (name)... hmm

// Better: extract scores, sort indices:
private _sorted = _leaderboard apply {
    private _entry = _x;
    [_entry get "score", _entry get "name"]
};
_sorted sort false;  // descending by score

// ---- Pattern 3: HashMap with Code values (method table) ----
private _unit = createHashMap;
_unit set ["hp", 100];
_unit set ["maxHp", 100];
_unit set ["takeDamage", {
    params ["_self", "_amount"];
    private _newHp = (_self get "hp") - _amount;
    if (_newHp < 0) then { _newHp = 0; };
    _self set ["hp", _newHp];
    _newHp
}];
_unit set ["heal", {
    params ["_self", "_amount"];
    private _max = _self get "maxHp";
    private _newHp = (_self get "hp") + _amount;
    if (_newHp > _max) then { _newHp = _max; };
    _self set ["hp", _newHp];
    _newHp
}];

// Call methods:
[_unit, 30] call (_unit get "takeDamage");  // hp = 70
[_unit, 15] call (_unit get "heal");        // hp = 85

// ---- Pattern 4: HashMap as cache/memo ----
private _fibCache = createHashMap;

global fn_fibMemo = {
    params ["_n"];
    if (_n <= 1) exitWith { _n };

    // Check cache:
    private _cached = _fibCache get _n;
    if (!isNil _cached) exitWith { _cached };

    // Compute and cache:
    private _result = (_n - 1) call fn_fibMemo + (_n - 2) call fn_fibMemo;
    _fibCache set [_n, _result];
    _result
};

// ---- Pattern 5: HashMap as set (keys only, values = true) ----
private _visited = createHashMap;
private _markVisited = {
    params ["_id"];
    _visited set [_id, true];
};
private _isVisited = {
    params ["_id"];
    _id in _visited
};

// ---- Pattern 6: HashMap for counting/frequency ----
private _freq = createHashMap;
private _items = ["apple", "banana", "apple", "orange", "banana", "apple"];
{
    private _count = _freq get _x;
    if (isNil _count) then { _count = 0; };
    _freq set [_x, _count + 1];
} forEach _items;
// _freq: {"apple"=3, "banana"=2, "orange"=1}

"data-structures complete"

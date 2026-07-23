// ============================================================
// arrays.sqf — Array creation, access, mutation, functional ops
// SQ# Language Sample
// ============================================================

// ---- Creation ----
private _empty = [];
private _nums = [1, 2, 3, 4, 5];
private _mixed = [42, "hello", true, nil];   // Heterogeneous
private _nested = [[1, 2], [3, 4], [5, 6]];
private _trailing = [1, 2, 3,];              // Trailing comma OK (SQ# fix)

// ---- Access ----
private _first = _nums select 0;     // 1 (zero-based)
private _second = _nums # 1;         // 2 (hash-select)
private _third = _nums[2];           // 3 (bracket access, SQ# preferred)

// Safe OOB access (SQ# fix):
private _safeOOB = _nums[99];        // nil (not error! unlike SQF)
private _safeNeg = _nums[-1];        // nil (safe)
private _paramSafe = _nums param [5, "default"];  // "default"

// ---- Mutation ----
private _arr = [10, 20, 30];

// pushBack — append one, returns new index:
private _idx = _arr pushBack 40;    // _idx = 3, _arr = [10,20,30,40]

// pushBackUnique — only if not already present:
_arr pushBackUnique 20;             // not added (already there)
_arr pushBackUnique 50;             // _arr = [10,20,30,40,50]

// append — bulk add:
_arr append [60, 70, 80];           // [10,20,30,40,50,60,70,80]

// set — index assignment (auto-resizes on positive OOB):
_arr set [0, 99];                   // [99,20,30,40,50,60,70,80]
_arr set [9, 100];                  // [99,20,30,40,50,60,70,80,nil,100]

// Bracket assignment (SQ# preferred):
_arr[1] = 200;                       // [99,200,30,40,50,60,70,80,nil,100]

// deleteAt — remove at index, returns deleted element:
private _removed = _arr deleteAt 0;  // _removed = 99

// deleteRange — remove n elements from index:
_arr deleteRange [3, 2];             // removes elements at index 3 and 4

// insert — insert at index (SQ# addition):
// _arr insert [2, 999];             // insert 999 at index 2

// resize — shrink or expand:
_arr resize 3;                       // truncate to 3 elements
_arr resize 5;                       // expand, fill with nil

// ---- Size & Search ----
private _len = count _arr;           // number of elements
private _isEmpty = (count _arr == 0);

private _pos = _arr find 30;         // index of 30, or -1
private _has = 30 in _arr;           // true if contains (SQ#: 'in' is nular)

// findIf — first match index (early exit, fast):
_arr = [5, 12, 8, 130, 44];
private _firstBig = _arr findIf { _x > 10 };   // 1 (value 12)

// ---- Copy ----
private _original = [1, 2, 3];

// Arrays are reference types:
private _ref = _original;           // _ref points to same array
_ref set [0, 99];                   // _original also changed!

// Shallow copy:
private _shallow = _original copy;  // new array, same elements
_shallow set [0, 1];                // _original unchanged

// Deep copy (recursively copies sub-arrays):
private _deep = +_original;         // SQF compat: unary + = deep copy
private _deep2 = _original deepCopy; // SQ# explicit

// Demo deep vs shallow:
private _nestedArr = [[1, 2], [3, 4]];
private _shallowNested = _nestedArr copy;
private _deepNested = +_nestedArr;
(_nestedArr select 0) set [0, 99];
// _shallowNested[0] = [99, 2]  — affected! (shallow)
// _deepNested[0] = [1, 2]      — unaffected (deep)

// ---- Functional Operations (return NEW arrays) ----
private _data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

// apply — map:
private _doubled = _data apply { _x * 2 };           // [2,4,6,...,20]
private _labels = _data apply { f"Item_{_x}" };      // string result

// select with code — filter:
private _evens = _data select { _x % 2 == 0 };       // [2,4,6,8,10]
private _bigOnes = _data select { _x > 5 };           // [6,7,8,9,10]

// reduce — fold/aggregate (SQ# addition):
// private _sum = _data reduce { _acc + _x } start 0;  // 55

// all / any — quantifiers (SQ# additions):
// private _allPositive = _data all { _x > 0 };        // true
// private _anyBig = _data any { _x > 8 };             // true

// ---- Array Arithmetic ----
private _a = [1, 2, 3];
private _b = [4, 5, 6];

// Concatenation:
private _combined = _a + _b;         // [1,2,3,4,5,6]

// Subtraction — removes ALL occurrences:
private _removeAll = [1,2,3,2,4,2] - [2,3];  // [1,4]
// All 2's and 3's removed!

// ---- Sorting ----
private _unsorted = [42, 10, 99, 5, 33];
_unsorted sort true;                 // ascending: [5,10,33,42,99]
_unsorted sort false;                // descending: [99,42,33,10,5]

// Sort sub-arrays by first element:
private _pairs = [["zzz", 0], ["aaa", 42], ["ccc", 33]];
_pairs sort true;                    // [["aaa",42], ["ccc",33], ["zzz",0]]

// Reverse:
private _rev = [1, 2, 3, 4, 5];
reverse _rev;                        // [5,4,3,2,1] (mutates in-place)

// ---- Array Intersection ----
private _set1 = [1, 2, 3, 4, 5];
private _set2 = [3, 4, 5, 6, 7];
private _intersect = _set1 arrayIntersect _set2;  // [3,4,5] (new array)
// Note: arrayIntersect also removes duplicates!

// ---- Freeze/Thaw (immutable snapshots, SQ# addition) ----
private _mutable = [1, 2, 3];
private _frozen = _mutable freeze;   // Immutable FrozenArray
// _frozen[0] = 5;                   // RUNTIME ERROR: frozen array
private _readOK = _frozen[0];        // 1 (read OK)
private _thawed = _frozen thaw;      // New mutable copy

// ---- joinString / splitString ----
private _parts = ["alpha", "bravo", "charlie"];
private _joined = _parts joinString ", ";  // "alpha, bravo, charlie"

private _csv = "a,b,c,d";
private _split = _csv splitString ",";     // ["a","b","c","d"]

"arrays complete"

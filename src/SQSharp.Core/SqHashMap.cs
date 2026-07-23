using System.Collections;
using System.Collections.Generic;

namespace SQSharp.Core;

/// <summary>
/// SQ# HashMap — key-value store. Keys must be hashable types
/// (value types, frozen arrays, or identity-hashed reference types).
/// Includes scheduler ownership tracking for thread safety.
/// </summary>
public sealed class SqHashMap : IEnumerable<KeyValuePair<SqValue, SqValue>>
{
    private readonly Dictionary<SqValue, SqValue> _map;
    private int _ownerSchedulerId;

    /// <summary>Scheduler that owns this hashmap. 0 = no owner.</summary>
    public int OwnerSchedulerId => _ownerSchedulerId;

    public SqHashMap(int ownerSchedulerId = 0)
    {
        _map = new Dictionary<SqValue, SqValue>(SqValueEqualityComparer.Instance);
        _ownerSchedulerId = ownerSchedulerId;
    }

    public int Count => _map.Count;

    public void Set(SqValue key, SqValue value)
    {
        ValidateKey(key);
        _map[key] = value;
    }

    public SqValue Get(SqValue key, SqValue @default = default)
    {
        return _map.TryGetValue(key, out var val) ? val : @default;
    }

    public bool ContainsKey(SqValue key) => _map.ContainsKey(key);

    public bool Remove(SqValue key) => _map.Remove(key);

    public void Clear() => _map.Clear();

    public IEnumerable<KeyValuePair<SqValue, SqValue>> Entries => _map;

    public IEnumerator<KeyValuePair<SqValue, SqValue>> GetEnumerator() => _map.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _map.GetEnumerator();

    private static void ValidateKey(SqValue key)
    {
        if (key.Type == SqType.Array)
            throw new SqTypeError("Mutable arrays cannot be used as HashMap keys. Use .freeze() first.");
    }
}

/// <summary>
/// Equality comparer for HashMap keys. Uses value equality for value types,
/// reference equality for reference types.
/// </summary>
internal sealed class SqValueEqualityComparer : IEqualityComparer<SqValue>
{
    public static readonly SqValueEqualityComparer Instance = new();

    public bool Equals(SqValue x, SqValue y) => x.Equals(y);

    public int GetHashCode(SqValue obj) => obj.GetHashCode();
}

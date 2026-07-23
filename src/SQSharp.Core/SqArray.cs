using System;
using System.Collections;
using System.Collections.Generic;

namespace SQSharp.Core;

/// <summary>
/// Mutable, dynamically-sized, heterogeneous array — the core SQF/SQ# array type.
/// Reference type (shared by reference when assigned).
/// Includes scheduler ownership tracking for thread safety.
/// </summary>
public sealed class SqArray : IReadOnlyList<SqValue>
{
    private SqValue[] _items;
    private int _count;
    private int _ownerSchedulerId;
    private bool _frozen;

    /// <summary>Scheduler that owns this array. 0 = no owner (frozen/shared).</summary>
    public int OwnerSchedulerId => _ownerSchedulerId;

    /// <summary>Whether this array is frozen (immutable).</summary>
    public bool IsFrozen => _frozen;

    public SqArray() : this(capacity: 4) { }

    public SqArray(int ownerSchedulerId) : this(capacity: 4) { _ownerSchedulerId = ownerSchedulerId; }

    public SqArray(int capacity, int ownerSchedulerId = 0)
    {
        _items = new SqValue[Math.Max(capacity, 4)];
        _count = 0;
        _ownerSchedulerId = ownerSchedulerId;
    }

    public SqArray(IEnumerable<SqValue> items, int ownerSchedulerId = 0)
    {
        _items = new List<SqValue>(items).ToArray();
        _count = _items.Length;
        _ownerSchedulerId = ownerSchedulerId;
    }

    // --- IReadOnlyList ---
    public int Count => _count;
    public SqValue this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                return SqValue.Nil;
            return _items[index];
        }
        set
        {
            ThrowIfFrozenOrForeign();
            if ((uint)index >= (uint)_count)
            {
                if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
                Resize(index + 1);
            }
            _items[index] = value;
        }
    }

    private void ThrowIfFrozenOrForeign()
    {
        if (_frozen) throw new InvalidOperationException("Cannot mutate frozen array. Use .Thaw() first.");
    }

    /// <summary>Freeze this array — makes it immutable, readable from any scheduler.</summary>
    public SqArray Freeze()
    {
        var frozen = new SqArray(_count, 0);
        Array.Copy(_items, frozen._items, _count);
        frozen._count = _count;
        frozen._frozen = true;
        return frozen;
    }

    /// <summary>Create a mutable copy of this (possibly frozen) array.</summary>
    public SqArray Thaw(int ownerSchedulerId)
    {
        var thawed = new SqArray(_count, ownerSchedulerId);
        Array.Copy(_items, thawed._items, _count);
        thawed._count = _count;
        return thawed;
    }

    // --- Mutation ---
    public void PushBack(SqValue value)
    {
        EnsureCapacity(_count + 1);
        _items[_count++] = value;
    }

    public void Append(SqArray other)
    {
        EnsureCapacity(_count + other._count);
        for (int i = 0; i < other._count; i++)
            _items[_count++] = other._items[i];
    }

    public SqValue DeleteAt(int index)
    {
        if ((uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var removed = _items[index];
        Array.Copy(_items, index + 1, _items, index, _count - index - 1);
        _items[--_count] = default;
        return removed;
    }

    public void DeleteRange(int index, int count)
    {
        if (index < 0 || count < 0 || index + count > _count)
            throw new ArgumentOutOfRangeException();
        Array.Copy(_items, index + count, _items, index, _count - index - count);
        for (int i = _count - count; i < _count; i++)
            _items[i] = default;
        _count -= count;
    }

    public void Insert(int index, SqValue value)
    {
        if ((uint)index > (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));
        EnsureCapacity(_count + 1);
        Array.Copy(_items, index, _items, index + 1, _count - index);
        _items[index] = value;
        _count++;
    }

    public void Resize(int newSize)
    {
        if (newSize < 0) throw new ArgumentOutOfRangeException(nameof(newSize));
        if (newSize > _count)
        {
            EnsureCapacity(newSize);
            Array.Fill(_items, SqValue.Nil, _count, newSize - _count);
        }
        else if (newSize < _count)
        {
            Array.Clear(_items, newSize, _count - newSize);
        }
        _count = newSize;
    }

    public int Find(SqValue value)
    {
        for (int i = 0; i < _count; i++)
            if (_items[i].Equals(value)) return i;
        return -1;
    }

    public void Sort(bool ascending = true)
    {
        Array.Sort(_items, 0, _count, ascending ? SqValueComparer.Ascending : SqValueComparer.Descending);
    }

    public void Reverse()
    {
        Array.Reverse(_items, 0, _count);
    }

    public SqArray Copy() // Shallow copy
    {
        var copy = new SqArray(_count);
        Array.Copy(_items, copy._items, _count);
        copy._count = _count;
        return copy;
    }

    public SqArray DeepCopy()
    {
        var copy = new SqArray(_count);
        for (int i = 0; i < _count; i++)
        {
            copy._items[i] = _items[i].Type == SqType.Array
                ? new SqValue(SqType.Array, _items[i].AsArray().DeepCopy())
                : _items[i];
        }
        copy._count = _count;
        return copy;
    }

    // --- Enumeration ---
    public IEnumerator<SqValue> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return _items[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // --- Internal ---
    private void EnsureCapacity(int min)
    {
        if (_items.Length < min)
        {
            int newSize = Math.Max(_items.Length * 2, min);
            Array.Resize(ref _items, newSize);
        }
    }

    internal SqValue[] RawItems => _items;
    internal int RawCount => _count;
}

/// <summary>
/// Default comparer for SqValue sorting.
/// Sorts by type group then by value: Nothing < Boolean < Number < String < Array...
/// </summary>
internal sealed class SqValueComparer : IComparer<SqValue>
{
    public static readonly SqValueComparer Ascending = new(true);
    public static readonly SqValueComparer Descending = new(false);

    private readonly bool _ascending;

    private SqValueComparer(bool ascending) => _ascending = ascending;

    public int Compare(SqValue x, SqValue y)
    {
        int typeCmp = x.Type.CompareTo(y.Type);
        if (typeCmp != 0) return _ascending ? typeCmp : -typeCmp;

        int valCmp = x.Type switch
        {
            SqType.Nothing => 0,
            SqType.Boolean => x.AsBool().CompareTo(y.AsBool()),
            SqType.Number => x.AsNumber().CompareTo(y.AsNumber()),
            SqType.String => string.CompareOrdinal(x.AsString(), y.AsString()),
            SqType.Array => x.AsArray().Count.CompareTo(y.AsArray().Count),
            _ => 0
        };
        return _ascending ? valCmp : -valCmp;
    }
}

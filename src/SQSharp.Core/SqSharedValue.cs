using System;
using System.Threading;

namespace SQSharp.Core;

/// <summary>
/// CAS-based (compare-and-swap) synchronized mutable value.
/// Created via the <c>shared</c> command. Supports atomic add, sub, set, get, compareSwap.
/// Uses Interlocked.CompareExchange for lock-free atomicity on double storage.
/// Booleans are stored as 0.0 / 1.0.
/// </summary>
public sealed class SqSharedValue
{
    // Internal storage — double supports Interlocked.CompareExchange natively
    private double _value;

    public SqSharedValue(double initial)
    {
        _value = initial;
    }

    public SqSharedValue(bool initial)
    {
        _value = initial ? 1.0 : 0.0;
    }

    /// <summary>Atomic read of the underlying value.</summary>
    public SqValue Get()
    {
        // Volatile.Read ensures fresh value (not cached in register)
        double val = Volatile.Read(ref _value);
        return new SqValue(val);
    }

    /// <summary>Atomic write of a new value.</summary>
    public SqValue Set(SqValue newValue)
    {
        double newVal = newValue.IsNumber ? newValue.AsNumber()
            : newValue.IsBool ? (newValue.AsBool() ? 1.0 : 0.0)
            : throw new SqTypeError($"Shared value does not support type {newValue.Type}");
        Volatile.Write(ref _value, newVal);
        return newValue;
    }

    /// <summary>Atomic add. Spin-waits on CAS failure. Returns NEW value after addition.</summary>
    public SqValue Add(double amount)
    {
        double current, desired;
        do
        {
            current = Volatile.Read(ref _value);
            desired = current + amount;
        }
        while (Interlocked.CompareExchange(ref _value, desired, current) != current);
        return new SqValue(desired);
    }

    /// <summary>Atomic subtract. Returns NEW value after subtraction.</summary>
    public SqValue Sub(double amount)
    {
        double current, desired;
        do
        {
            current = Volatile.Read(ref _value);
            desired = current - amount;
        }
        while (Interlocked.CompareExchange(ref _value, desired, current) != current);
        return new SqValue(desired);
    }

    /// <summary>
    /// Atomic compare-and-swap.
    /// [expected, newValue] — if current equals expected, set to newValue.
    /// Returns true if swap succeeded.
    /// </summary>
    public SqValue CompareSwap(SqValue expected, SqValue newValue)
    {
        double exp = expected.IsNumber ? expected.AsNumber()
            : expected.IsBool ? (expected.AsBool() ? 1.0 : 0.0)
            : throw new SqTypeError("Shared compareSwap: expected must be Number or Boolean");

        double desired = newValue.IsNumber ? newValue.AsNumber()
            : newValue.IsBool ? (newValue.AsBool() ? 1.0 : 0.0)
            : throw new SqTypeError("Shared compareSwap: new value must be Number or Boolean");

        return new SqValue(Interlocked.CompareExchange(ref _value, desired, exp) == exp);
    }

    public override string ToString() => $"Shared({_value})";
}

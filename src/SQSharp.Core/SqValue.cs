using System;

namespace SQSharp.Core;

/// <summary>
/// The universal value type for SQ# — wraps all possible SQF/SQ# values.
/// Uses a simple struct layout (no overlap) for GC safety.
/// </summary>
public readonly struct SqValue : IEquatable<SqValue>
{
    private readonly SqType _type;
    private readonly double _number;
    private readonly object? _object;

    // --- Constants ---
    public static SqValue Nil => new(SqType.Nothing);
    public static SqValue True => new(true);
    public static SqValue False => new(false);
    public static SqValue Zero => new(0.0);

    // --- Constructors ---
    private SqValue(SqType type)
    {
        _type = type;
        _number = 0.0;
        _object = null;
    }

    public SqValue(bool value)
    {
        _type = SqType.Boolean;
        _number = value ? 1.0 : 0.0;
        _object = null;
    }

    public SqValue(double value)
    {
        _type = SqType.Number;
        _number = value;
        _object = null;
    }

    public SqValue(string value)
    {
        _type = SqType.String;
        _number = 0;
        _object = value ?? throw new ArgumentNullException(nameof(value));
    }

    public SqValue(SqType type, object? obj, double num = 0.0)
    {
        _type = type;
        _number = num;
        _object = obj;
    }

    // --- Properties ---
    public SqType Type => _type;

    public bool IsNil => _type == SqType.Nothing;
    public bool IsBool => _type == SqType.Boolean;
    public bool IsNumber => _type == SqType.Number;
    public bool IsString => _type == SqType.String;
    public bool IsArray => _type == SqType.Array;
    public bool IsCode => _type == SqType.Code;
    public bool IsHashMap => _type == SqType.HashMap;
    public bool IsNothing => _type == SqType.Nothing;

    // --- Accessors ---
    public bool AsBool()
    {
        if (_type != SqType.Boolean)
            throw new SqTypeError($"Expected Boolean, got {_type}");
        return _number != 0.0;
    }

    public bool AsBoolOrDefault(bool @default = false)
    {
        return _type == SqType.Boolean ? _number != 0.0 : @default;
    }

    public double AsNumber()
    {
        if (_type != SqType.Number)
            throw new SqTypeError($"Expected Number, got {_type}");
        return _number;
    }

    public double AsNumberOrDefault(double @default = 0.0)
    {
        return _type == SqType.Number ? _number : @default;
    }

    public string AsString()
    {
        if (_type != SqType.String)
            throw new SqTypeError($"Expected String, got {_type}");
        return (string)_object!;
    }

    public string? AsStringOrDefault(string? @default = null)
    {
        return _type == SqType.String ? (string)_object! : @default;
    }

    public SqArray AsArray()
    {
        if (_type != SqType.Array)
            throw new SqTypeError($"Expected Array, got {_type}");
        return (SqArray)_object!;
    }

    public SqArray? AsArrayOrDefault()
    {
        return _type == SqType.Array ? (SqArray)_object! : null;
    }

    public SqCode AsCode()
    {
        if (_type != SqType.Code)
            throw new SqTypeError($"Expected Code, got {_type}");
        return (SqCode)_object!;
    }

    public SqCode? AsCodeOrDefault()
    {
        return _type == SqType.Code ? (SqCode)_object! : null;
    }

    public object? RawObject => _object;

    // --- Conversions (C# convenience) ---
    public static implicit operator SqValue(bool v) => new(v);
    public static implicit operator SqValue(double v) => new(v);
    public static implicit operator SqValue(string v) => new(v);

    public override string ToString()
    {
        return _type switch
        {
            SqType.Nothing => "nil",
            SqType.Boolean => _number != 0.0 ? "true" : "false",
            SqType.Number => _number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SqType.String => $"\"{_object}\"",
            SqType.Array => $"[{((SqArray)_object!).Count}]",
            SqType.Code => "{code}",
            SqType.HashMap => "{hashmap}",
            SqType.Error => $"<error: {_object}>",
            SqType.FrozenArray => $"<frozen[{((SqArray)_object!).Count}]>",
            _ => $"<{_type}>"
        };
    }

    // --- Equality ---
    public bool Equals(SqValue other)
    {
        if (_type != other._type) return false;
        return _type switch
        {
            SqType.Nothing => true,
            SqType.Boolean => _number == other._number,
            SqType.Number => _number == other._number,
            SqType.String => string.Equals((string?)_object, (string?)other._object, StringComparison.Ordinal),
            _ => ReferenceEquals(_object, other._object)
        };
    }

    public override bool Equals(object? obj) => obj is SqValue other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_type, _object ?? _number);
    public static bool operator ==(SqValue left, SqValue right) => left.Equals(right);
    public static bool operator !=(SqValue left, SqValue right) => !left.Equals(right);
}

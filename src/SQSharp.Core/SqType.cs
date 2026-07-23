namespace SQSharp.Core;

/// <summary>
/// Represents the runtime type of an SqValue.
/// </summary>
public enum SqType : byte
{
    /// <summary>No value (nil).</summary>
    Nothing = 0,

    /// <summary>Boolean true/false.</summary>
    Boolean = 1,

    /// <summary>IEEE 754 double-precision float.</summary>
    Number = 2,

    /// <summary>UTF-16 string (interned).</summary>
    String = 3,

    /// <summary>Dynamic mutable array (by reference).</summary>
    Array = 4,

    /// <summary>Compiled code block (first-class).</summary>
    Code = 5,

    /// <summary>Key-value hash map.</summary>
    HashMap = 6,

    /// <summary>Namespace (key-value global container).</summary>
    Namespace = 7,

    /// <summary>Script handle / promise.</summary>
    ScriptHandle = 8,

    /// <summary>Runtime error value.</summary>
    Error = 9,

    /// <summary>Immutable (frozen) array.</summary>
    FrozenArray = 10,

    /// <summary>Message channel for cross-scheduler communication.</summary>
    Channel = 11,

    /// <summary>Synchronized mutable wrapper (CAS-based).</summary>
    Shared = 12,

    /// <summary>Scheduler reference.</summary>
    Scheduler = 13,

    // --- Host-defined opaque types (128+) ---

    /// <summary>Host-registered type base offset.</summary>
    HostTypeBase = 128,
}

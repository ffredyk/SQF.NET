using System.Collections;
using System.Collections.Generic;

namespace SQSharp.Core;

/// <summary>
/// SQ# Namespace — a key-value container for global variables.
/// Each scheduler has its own copy of missionNamespace.
/// </summary>
public sealed class SqNamespace : IEnumerable<KeyValuePair<string, SqValue>>
{
    private readonly Dictionary<string, SqValue> _variables = new();
    private readonly Dictionary<string, SqValue> _variablesCI = new(StringComparer.OrdinalIgnoreCase);

    public string Name { get; }

    /// <summary>Whether this namespace is serialized (e.g., missionNamespace for saveGame).</summary>
    public bool IsSerialized { get; }

    /// <summary>Whether variable lookup is case-insensitive (SQF compat).</summary>
    public bool CaseInsensitive { get; }

    public SqNamespace(string name, bool isSerialized = false, bool caseInsensitive = true)
    {
        Name = name;
        IsSerialized = isSerialized;
        CaseInsensitive = caseInsensitive;
    }

    public void SetVariable(string name, SqValue value)
    {
        if (CaseInsensitive)
        {
            _variablesCI[name] = value;
            _variables[name] = value;
        }
        else
        {
            _variables[name] = value;
        }
    }

    public SqValue GetVariable(string name)
    {
        if (CaseInsensitive)
            return _variablesCI.TryGetValue(name, out var v) ? v : SqValue.Nil;
        return _variables.TryGetValue(name, out var val) ? val : SqValue.Nil;
    }

    public bool HasVariable(string name)
    {
        return CaseInsensitive ? _variablesCI.ContainsKey(name) : _variables.ContainsKey(name);
    }

    public void DeleteVariable(string name)
    {
        _variables.Remove(name);
        _variablesCI.Remove(name);
    }

    public int Count => _variables.Count;

    public IEnumerable<string> AllVariables => _variables.Keys;

    public IEnumerator<KeyValuePair<string, SqValue>> GetEnumerator()
    {
        foreach (var kv in _variables)
            yield return kv;
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

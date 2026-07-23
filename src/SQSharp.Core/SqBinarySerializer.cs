using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SQSharp.Core;

/// <summary>
/// Binary serialization for SQ# values, arrays, hashmaps, and bytecode.
/// Used by hosts for game save/load, state snapshot, and network transfer.
/// 
/// Format: tagged binary with length-prefixed strings and recursive compound types.
/// All multi-byte values are little-endian.
/// 
/// Does NOT serialize runtime objects (fibers, schedulers, channels) — 
/// those are handled by the host and SQSharp.Scheduler.
/// </summary>
public static class SqBinarySerializer
{
    private const byte TagNil = 0;
    private const byte TagBoolean = 1;
    private const byte TagNumber = 2;
    private const byte TagString = 3;
    private const byte TagArray = 4;
    private const byte TagCode = 5;
    private const byte TagHashMap = 6;
    private const byte TagShared = 7;
    private const byte TagError = 8;
    private const byte TagFrozenArray = 9;
    private const byte TagNamespace = 10;
    private const byte TagScriptHandle = 11;
    private const byte TagScheduler = 12;

    // --- SqValue ---

    public static void WriteValue(BinaryWriter w, SqValue value)
    {
        switch (value.Type)
        {
            case SqType.Nothing:
                w.Write(TagNil);
                break;
            case SqType.Boolean:
                w.Write(TagBoolean);
                w.Write(value.AsBoolOrDefault() ? (byte)1 : (byte)0);
                break;
            case SqType.Number:
                w.Write(TagNumber);
                w.Write(value.AsNumberOrDefault());
                break;
            case SqType.String:
                w.Write(TagString);
                WriteString(w, value.AsString());
                break;
            case SqType.Array:
                w.Write(TagArray);
                WriteArray(w, value.AsArray());
                break;
            case SqType.Code:
                w.Write(TagCode);
                WriteCode(w, value.AsCode());
                break;
            case SqType.HashMap:
                w.Write(TagHashMap);
                WriteHashMap(w, (SqHashMap)value.RawObject!);
                break;
            case SqType.Shared:
                w.Write(TagShared);
                w.Write(((SqSharedValue)value.RawObject!).Get().AsNumberOrDefault());
                break;
            case SqType.Error:
                w.Write(TagError);
                WriteString(w, value.RawObject?.ToString() ?? "unknown error");
                break;
            case SqType.FrozenArray:
                w.Write(TagFrozenArray);
                WriteArray(w, value.AsArray());
                break;
            case SqType.Namespace:
                w.Write(TagNamespace);
                WriteNamespace(w, (SqNamespace)value.RawObject!);
                break;
            case SqType.ScriptHandle:
                w.Write(TagScriptHandle);
                w.Write((byte)0); // not resolved — host restores
                break;
            case SqType.Scheduler:
                w.Write(TagScheduler);
                w.Write((int)value.AsNumberOrDefault());
                break;
            case SqType.Channel:
                w.Write(TagNil); // channels not serializable
                break;
            default:
                int hostTag = value.Type - SqType.HostTypeBase;
                if (hostTag >= 0 && hostTag < 128)
                {
                    w.Write((byte)(hostTag + 128));
                    WriteString(w, value.RawObject?.ToString() ?? "");
                }
                else w.Write(TagNil);
                break;
        }
    }

    public static SqValue ReadValue(BinaryReader r)
    {
        byte tag = r.ReadByte();
        return tag switch
        {
            TagNil => SqValue.Nil,
            TagBoolean => new SqValue(r.ReadByte() != 0),
            TagNumber => new SqValue(r.ReadDouble()),
            TagString => new SqValue(ReadString(r)),
            TagArray => new SqValue(SqType.Array, ReadArray(r)),
            TagCode => ReadCode(r),
            TagHashMap => new SqValue(SqType.HashMap, ReadHashMap(r)),
            TagShared => new SqValue(SqType.Shared, new SqSharedValue(r.ReadDouble())),
            TagError => new SqValue(SqType.Error, ReadString(r), 0.0),
            TagFrozenArray => new SqValue(SqType.FrozenArray, ReadArray(r)),
            TagNamespace => ReadNamespace(r),
            TagScriptHandle => ReadScriptHandle(r),
            TagScheduler => new SqValue((double)r.ReadInt32()),
            >= 128 => ReadHostType(r, tag),
            _ => SqValue.Nil
        };
    }

    // --- Convenience: byte array API ---

    public static byte[] Serialize(SqValue value)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        WriteValue(w, value);
        w.Flush();
        return ms.ToArray();
    }

    public static SqValue Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var r = new BinaryReader(ms, Encoding.UTF8);
        return ReadValue(r);
    }

    // --- Compound type writers ---

    public static void WriteArray(BinaryWriter w, SqArray arr)
    {
        w.Write(arr.Count);
        w.Write(arr.OwnerSchedulerId);
        w.Write(arr.IsFrozen ? (byte)1 : (byte)0);
        foreach (var item in arr)
            WriteValue(w, item);
    }

    public static SqArray ReadArray(BinaryReader r)
    {
        int count = r.ReadInt32();
        int ownerId = r.ReadInt32();
        bool frozen = r.ReadByte() != 0;
        var arr = new SqArray(capacity: count, ownerSchedulerId: ownerId);
        for (int i = 0; i < count; i++)
            arr.PushBack(ReadValue(r));
        if (frozen) arr.Freeze();
        return arr;
    }

    public static void WriteHashMap(BinaryWriter w, SqHashMap map)
    {
        var pairs = new List<KeyValuePair<SqValue, SqValue>>();
        foreach (var kv in map) pairs.Add(kv);
        w.Write(pairs.Count);
        w.Write(map.OwnerSchedulerId);
        foreach (var kv in pairs)
        {
            WriteValue(w, kv.Key);
            WriteValue(w, kv.Value);
        }
    }

    public static SqHashMap ReadHashMap(BinaryReader r)
    {
        int pairCount = r.ReadInt32();
        int ownerId = r.ReadInt32();
        var map = new SqHashMap(ownerSchedulerId: ownerId);
        for (int i = 0; i < pairCount; i++)
        {
            var key = ReadValue(r);
            var val = ReadValue(r);
            map.Set(key, val);
        }
        return map;
    }

    // --- Bytecode chunk ---

    public static void WriteChunk(BinaryWriter w, BytecodeChunk chunk)
    {
        w.Write(chunk.Instructions.Count);
        foreach (var inst in chunk.Instructions)
        {
            w.Write((byte)inst.OpCode);
            w.Write(inst.Operand);
            w.Write(inst.Operand2);
        }
        w.Write(chunk.Constants.Count);
        foreach (var c in chunk.Constants)
            WriteValue(w, c);
        w.Write(chunk.GlobalNames.Count);
        foreach (var name in chunk.GlobalNames)
            WriteString(w, name);
        w.Write(chunk.CommandIds.Count);
        foreach (var cmdId in chunk.CommandIds)
            w.Write(cmdId);
        w.Write(chunk.Children.Count);
        foreach (var child in chunk.Children)
            WriteChunk(w, child);
        w.Write(chunk.LocalCount);
        WriteString(w, chunk.SourceFile ?? "");
    }

    public static BytecodeChunk ReadChunk(BinaryReader r)
    {
        var chunk = new BytecodeChunk();
        int instCount = r.ReadInt32();
        for (int i = 0; i < instCount; i++)
        {
            var op = (OpCode)r.ReadByte();
            int operand = r.ReadInt32();
            int operand2 = r.ReadInt32();
            chunk.Emit(op, operand, operand2);
        }
        int constCount = r.ReadInt32();
        for (int i = 0; i < constCount; i++)
            chunk.AddConstant(ReadValue(r));
        int nameCount = r.ReadInt32();
        for (int i = 0; i < nameCount; i++)
            chunk.AddGlobal(ReadString(r));
        int cmdCount = r.ReadInt32();
        for (int i = 0; i < cmdCount; i++)
            r.ReadInt32(); // skip — rebuilt at runtime
        int childCount = r.ReadInt32();
        for (int i = 0; i < childCount; i++)
            chunk.Children.Add(ReadChunk(r));
        chunk.LocalCount = r.ReadInt32();
        chunk.SourceFile = ReadString(r);
        return chunk;
    }

    // --- Private helpers ---

    private static void WriteCode(BinaryWriter w, SqCode code)
    {
        WriteString(w, code.SourceText ?? "");
        bool hasChunk = code.CompiledChunk != null;
        w.Write(hasChunk ? (byte)1 : (byte)0);
        if (hasChunk) WriteChunk(w, code.CompiledChunk!);
    }

    private static SqValue ReadCode(BinaryReader r)
    {
        string source = ReadString(r);
        bool hasChunk = r.ReadByte() != 0;
        BytecodeChunk? chunk = hasChunk ? ReadChunk(r) : null;
        return new SqValue(SqType.Code, new SqCode(source, compiledChunk: chunk));
    }

    private static void WriteNamespace(BinaryWriter w, SqNamespace ns)
    {
        var pairs = new List<KeyValuePair<string, SqValue>>();
        foreach (var kv in ns) pairs.Add(kv);
        w.Write(pairs.Count);
        foreach (var kv in pairs)
        {
            WriteString(w, kv.Key);
            WriteValue(w, kv.Value);
        }
    }

    private static SqValue ReadNamespace(BinaryReader r)
    {
        int count = r.ReadInt32();
        var ns = new SqNamespace("", isSerialized: true);
        for (int i = 0; i < count; i++)
        {
            string name = ReadString(r);
            ns.SetVariable(name, ReadValue(r));
        }
        return new SqValue(SqType.Namespace, ns);
    }

    private static SqValue ReadScriptHandle(BinaryReader r)
    {
        r.ReadByte(); // resolved flag — host restores from fiber
        return SqValue.Nil;
    }

    private static SqValue ReadHostType(BinaryReader r, byte tag)
    {
        SqType hostType = (SqType)((int)SqType.HostTypeBase + (tag - 128));
        return new SqValue(hostType, ReadString(r));
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        w.Write(bytes.Length);
        w.Write(bytes);
    }

    private static string ReadString(BinaryReader r)
    {
        int len = r.ReadInt32();
        return Encoding.UTF8.GetString(r.ReadBytes(len));
    }
}

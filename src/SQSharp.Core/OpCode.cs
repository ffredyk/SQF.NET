namespace SQSharp.Core;

/// <summary>
/// Bytecode operation codes for the SQ# stack VM.
/// Shared between Compiler and VM.
/// </summary>
public enum OpCode : byte
{
    PushConst = 1,
    PushLocal,
    StoreLocal,
    PushGlobal,
    StoreGlobal,
    NularCall,
    UnaryCall,
    BinaryCall,
    MakeArray,
    MakeHashMap,
    MakeCode,
    Jump,
    JumpIfFalse,
    JumpIfTrue,
    Call,
    Spawn,
    Ret,
    Yield,
    Dup,
    Pop,
    Swap,
    SpawnOn,
    Await,
    Throw,
    TryBegin,
    TryEnd,
    MakeShared,  // pop value, create SqSharedValue, push back
}

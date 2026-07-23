using Xunit;
using SQSharp.Language;
using SQSharp.Compiler;
using SQSharp.VM;
using SQSharp.Core;

namespace SQSharp.VM.Tests;

public class ExecutionTests
{
    // === Literals ===
    [Fact]
    public void Number_ReturnsValue() => Assert.Equal(42.0, Run("42").AsNumber());

    [Fact]
    public void String_ReturnsValue() => Assert.Equal("hello", Run("\"hello\"").AsString());

    [Fact]
    public void Bool_ReturnsTrue() => Assert.True(Run("true").AsBool());

    [Fact]
    public void Nil_ReturnsNil() => Assert.Equal(SqType.Nothing, Run("nil").Type);

    // === Variables and assignment ===
    [Fact]
    public void VariableAssignment()
    {
        Assert.Equal(5.0, Run("_x = 5; _x").AsNumber());
    }

    [Fact]
    public void MultipleAssignments()
    {
        Assert.Equal(30.0, Run("_num = 10; _num = _num + 20; _num").AsNumber());
    }

    // === Arithmetic ===
    [Fact]
    public void Addition() => Assert.Equal(8.0, Run("5 + 3").AsNumber());

    [Fact]
    public void Subtraction() => Assert.Equal(2.0, Run("5 - 3").AsNumber());

    [Fact]
    public void Multiplication() => Assert.Equal(15.0, Run("5 * 3").AsNumber());

    [Fact]
    public void Division() => Assert.Equal(2.0, Run("6 / 3").AsNumber());

    [Fact]
    public void Precedence_MulBeforeAdd()
    {
        // 1 + 2 * 3 = 7 (not 9)
        Assert.Equal(7.0, Run("1 + 2 * 3").AsNumber());
    }

    [Fact]
    public void Precedence_ParensOverride()
    {
        Assert.Equal(9.0, Run("(1 + 2) * 3").AsNumber());
    }

    // === Comparison ===
    [Fact]
    public void Equal_True() => Assert.True(Run("5 == 5").AsBool());

    [Fact]
    public void Equal_False() => Assert.False(Run("5 == 3").AsBool());

    [Fact]
    public void NotEqual() => Assert.True(Run("5 != 3").AsBool());

    [Fact]
    public void LessThan() => Assert.True(Run("3 < 5").AsBool());

    // === Array operations ===
    [Fact]
    public void ArrayCreation()
    {
        var result = Run("[1, 2, 3]");
        Assert.Equal(SqType.Array, result.Type);
        Assert.Equal(3, result.AsArray().Count);
    }

    [Fact]
    public void PushBack_ReturnsIndex()
    {
        Assert.Equal(2.0, Run("_arr = [10, 20]; _arr pushBack 30").AsNumber());
    }

    [Fact]
    public void Select_AccessesElement()
    {
        Assert.Equal(20.0, Run("_arr = [10, 20, 30]; _arr select 1").AsNumber());
    }

    [Fact]
    public void Count_Array()
    {
        Assert.Equal(3.0, Run("count [10, 20, 30]").AsNumber());
    }

    // === Terminators ===
    [Fact]
    public void Semicolon_Terminates()
    {
        Assert.Equal(3.0, Run("_a = 1; _b = 2; _a + _b").AsNumber());
    }

    [Fact]
    public void Comma_Terminates()
    {
        Assert.Equal(3.0, Run("_a = 1, _b = 2, _a + _b").AsNumber());
    }

    // === Comments ===
    [Fact]
    public void BlockComment_MidExpression()
    {
        Assert.Equal(2.0, Run("1 + /* comment */ 1").AsNumber());
    }

    // === Unary greediness (SQF Syntax wiki verified) ===
    [Fact]
    public void UnaryGreed_CountConsumesArray()
    {
        // count _arr select 2 = (count _arr) select 2
        // count returns number, select on number returns nil (graceful fallback)
        var result = Run("_arr = [[1,2],[3,4]]; count _arr select 2");
        Assert.True(result.IsNil);
    }

    [Fact]
    public void ParensOverride_UnaryGreed()
    {
        // count (_arr select 2) = correct
        Assert.Equal(3.0, Run("_arr = [[1,2],[3,4,5]]; count (_arr select 1)").AsNumber());
    }

    [Fact]
    public void LocalVar_NotUnaryCommand()
    {
        // _arr pushBack 6 — _arr is local, pushBack is binary
        Assert.Equal(2.0, Run("_arr = [10, 20]; _arr pushBack 30").AsNumber());
    }

    // === Call / Spawn ===
    [Fact]
    public void Call_WithArgs_ExecutesCodeBlock()
    {
        // [1,2] call { (_this select 0) + (_this select 1) } = 3
        var result = Run("[1, 2] call { (_this select 0) + (_this select 1) }");
        // Call returns a value; verify it's not an error
        Assert.NotEqual(SqType.Error, result.Type);
    }

    [Fact]
    public void Call_ReturnsConstantValue()
    {
        // call with code that just returns a constant
        var result = Run("call { 42 }");
        Assert.Equal(42.0, result.AsNumber());
    }

    [Fact]
    public void Call_WithArgs_ReturnsComputedValue()
    {
        var result = Run("[100, 200] call { (_this select 0) + (_this select 1) }");
        Assert.Equal(300.0, result.AsNumber());
    }

    // === Return ===
    [Fact]
    public void Return_EarlyExit()
    {
        var result = Run("return 42; _x = 1");
        Assert.Equal(42.0, result.AsNumber());
    }

    // === Constant folding (verified at runtime) ===
    [Fact]
    public void ConstantFolding_Addition()
    {
        // 1 + 2 should fold to 3 at compile time
        Assert.Equal(7.0, Run("1 + 2 * 3").AsNumber()); // 1 + 6 = 7
    }

    [Fact]
    public void ConstantFolding_Arithmetic()
    {
        Assert.Equal(7.0, Run("3 + 4").AsNumber());
        Assert.Equal(6.0, Run("2 * 3").AsNumber());
    }

    // === Import (no-op at runtime, just tests parsing+compilation) ===
    [Fact]
    public void Import_DoesNotCrash()
    {
        var result = Run("import \"nonexistent\"");
        Assert.True(result.IsNil);
    }

    // === Params ===
    [Fact]
    public void Params_SimpleNames_AssignsFromThis()
    {
        // params reads from _this (local slot 0)
        // In bare VM without call, _this is nil → select returns nil → isNil → no defaults → nil
        var result = Run("params [\"_a\", \"_b\"]; _a");
        Assert.True(result.IsNil);
    }

    [Fact]
    public void Params_WithDefaults_UsesDefaultWhenNil()
    {
        // _this is nil, so both params get defaults
        var result = Run("params [[\"_a\", 10], [\"_b\", 20]]; _a + _b");
        Assert.Equal(30.0, result.AsNumber());
    }

    [Fact]
    public void Params_Mixed_SomeDefaults()
    {
        var result = Run("params [[\"_a\", 5], \"_b\"]; _a");
        Assert.Equal(5.0, result.AsNumber());
    }

    [Fact]
    public void Params_ViaCall_UsesThisArray()
    {
        // [100, 200] call { params ["_a", "_b"]; _a + _b } → 300
        var result = Run("[100, 200] call { params [\"_a\", \"_b\"]; _a + _b }");
        Assert.Equal(300.0, result.AsNumber());
    }

    [Fact]
    public void Params_ViaCall_WithDefaults_PartialArray()
    {
        // [100] call { params ["_a", [\"_b\", 999]]; _a + _b } → 100 + 999 = 1099
        var result = Run("[100] call { params [\"_a\", [\"_b\", 999]]; _a + _b }");
        Assert.Equal(1099.0, result.AsNumber());
    }

    // === Helpers ===
    private static SqValue Run(string source)
    {
        var lexer = new Lexer(source, legacyComments: true);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        var compiler = new SQSharp.Compiler.Compiler();
        var chunk = compiler.Compile(ast);
        var vm = new SqVm(chunk);
        return vm.Execute();
    }
}

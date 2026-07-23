using Xunit;
using SQSharp.Core;

namespace SQSharp.Core.Tests;

public class SqValueTests
{
    [Fact]
    public void Nil_IsNothing()
    {
        var v = SqValue.Nil;
        Assert.Equal(SqType.Nothing, v.Type);
        Assert.True(v.IsNil);
    }

    [Fact]
    public void Boolean_StoresCorrectly()
    {
        Assert.True(new SqValue(true).AsBool());
        Assert.False(new SqValue(false).AsBool());
    }

    [Fact]
    public void Number_StoresCorrectly()
    {
        Assert.Equal(42.0, new SqValue(42.0).AsNumber());
        Assert.Equal(-3.14, new SqValue(-3.14).AsNumber());
    }

    [Fact]
    public void String_StoresCorrectly()
    {
        Assert.Equal("hello", new SqValue("hello").AsString());
    }

    [Fact]
    public void Nil_Equals_Nil()
    {
        Assert.True(SqValue.Nil.Equals(SqValue.Nil));
        Assert.True(SqValue.Nil == SqValue.Nil);
    }

    [Fact]
    public void Equality_Works()
    {
        Assert.True(new SqValue(1.0).Equals(new SqValue(1.0)));
        Assert.True(new SqValue(true).Equals(new SqValue(true)));
        Assert.True(new SqValue("hi").Equals(new SqValue("hi")));
    }

    [Fact]
    public void Inequality_Works()
    {
        Assert.True(new SqValue(1.0) != new SqValue(2.0));
        Assert.True(new SqValue(true) != new SqValue(false));
        Assert.False(new SqValue(1.0).Equals(new SqValue(true)));
    }

    [Fact]
    public void TypeChecks_AreCorrect()
    {
        Assert.True(new SqValue(true).IsBool);
        Assert.True(new SqValue(1.0).IsNumber);
        Assert.True(new SqValue("x").IsString);
    }

    [Fact]
    public void WrongTypeAccess_Throws()
    {
        Assert.Throws<SqTypeError>(() => SqValue.Nil.AsBool());
        Assert.Throws<SqTypeError>(() => new SqValue(true).AsNumber());
    }

    [Fact]
    public void DefaultAccessors_ReturnDefault()
    {
        Assert.Equal(0.0, SqValue.Nil.AsNumberOrDefault());
        Assert.False(SqValue.Nil.AsBoolOrDefault());
        Assert.Null(SqValue.Nil.AsStringOrDefault());
        Assert.Equal(42.0, SqValue.Nil.AsNumberOrDefault(42.0));
    }
}

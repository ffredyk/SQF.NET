using Xunit;
using SQSharp.Core;

namespace SQSharp.Core.Tests;

public class SqArrayTests
{
    [Fact]
    public void NewArray_IsEmpty()
    {
        var arr = new SqArray();
        Assert.Equal(0, arr.Count);
    }

    [Fact]
    public void PushBack_AddsElement()
    {
        var arr = new SqArray();
        arr.PushBack(new SqValue(1.0));
        arr.PushBack(new SqValue(2.0));
        Assert.Equal(2, arr.Count);
        Assert.Equal(1.0, arr[0].AsNumber());
        Assert.Equal(2.0, arr[1].AsNumber());
    }

    [Fact]
    public void IndexAccess_ReturnsElement()
    {
        var arr = new SqArray();
        arr.PushBack(new SqValue(10.0));
        arr.PushBack(new SqValue(20.0));
        Assert.Equal(10.0, arr[0].AsNumber());
        Assert.Equal(20.0, arr[1].AsNumber());
    }

    [Fact]
    public void OutOfBounds_ReturnsNil()
    {
        var arr = new SqArray();
        arr.PushBack(new SqValue(1.0));
        Assert.Equal(SqType.Nothing, arr[99].Type);
        Assert.Equal(SqType.Nothing, arr[-1].Type);
    }

    [Fact]
    public void Set_ReplacesElement()
    {
        var arr = new SqArray();
        arr.PushBack(new SqValue(1.0));
        arr[0] = new SqValue(99.0);
        Assert.Equal(99.0, arr[0].AsNumber());
    }

    [Fact]
    public void Set_OutOfBounds_AutoResizes()
    {
        var arr = new SqArray();
        arr[3] = new SqValue(42.0);
        Assert.Equal(4, arr.Count);
        Assert.Equal(42.0, arr[3].AsNumber());
        Assert.Equal(SqType.Nothing, arr[0].Type); // gap filled with nil
    }

    [Fact]
    public void DeleteAt_RemovesElement()
    {
        var arr = new SqArray();
        arr.PushBack(new SqValue(1.0));
        arr.PushBack(new SqValue(2.0));
        arr.PushBack(new SqValue(3.0));
        var removed = arr.DeleteAt(1);
        Assert.Equal(2.0, removed.AsNumber());
        Assert.Equal(2, arr.Count);
        Assert.Equal(1.0, arr[0].AsNumber());
        Assert.Equal(3.0, arr[1].AsNumber());
    }

    [Fact]
    public void Resize_Truncates()
    {
        var arr = new SqArray();
        arr.PushBack(new SqValue(1.0));
        arr.PushBack(new SqValue(2.0));
        arr.PushBack(new SqValue(3.0));
        arr.Resize(1);
        Assert.Equal(1, arr.Count);
    }

    [Fact]
    public void Resize_ExpandsWithNil()
    {
        var arr = new SqArray();
        arr.PushBack(new SqValue(1.0));
        arr.Resize(3);
        Assert.Equal(3, arr.Count);
        Assert.Equal(SqType.Nothing, arr[1].Type);
    }

    [Fact]
    public void Copy_CreatesIndependentCopy()
    {
        var arr = new SqArray();
        arr.PushBack(new SqValue(1.0));
        var copy = arr.Copy();
        arr[0] = new SqValue(99.0);
        Assert.Equal(1.0, copy[0].AsNumber()); // not affected
    }

    [Fact]
    public void Find_ReturnsIndex()
    {
        var arr = new SqArray();
        arr.PushBack(new SqValue(10.0));
        arr.PushBack(new SqValue(20.0));
        Assert.Equal(1, arr.Find(new SqValue(20.0)));
        Assert.Equal(-1, arr.Find(new SqValue(99.0)));
    }

    [Fact]
    public void Append_AddsRange()
    {
        var arr = new SqArray();
        arr.PushBack(new SqValue(1.0));
        var other = new SqArray();
        other.PushBack(new SqValue(2.0));
        other.PushBack(new SqValue(3.0));
        arr.Append(other);
        Assert.Equal(3, arr.Count);
        Assert.Equal(3.0, arr[2].AsNumber());
    }
}

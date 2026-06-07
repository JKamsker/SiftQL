using SiftQL.Expressions;
using SiftQL.Index;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterIndexValueTests
{
    public enum TestStatus { None = 0, Active = 1, Inactive = 2 }

    [Fact] public void FilterIndexValue_TryCreate_Boolean_Succeeds()
    {
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(true), out var key));
        Assert.Equal(FilterValueKind.Boolean, key.Kind);
        Assert.True(key.Boolean);
    }

    [Fact] public void FilterIndexValue_TryCreate_Integer_Succeeds()
    {
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(42L), out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
        Assert.Equal(42L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreate_Number_Succeeds()
    {
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(3.14), out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
        Assert.Equal(3.14, key.Number);
    }

    [Fact] public void FilterIndexValue_TryCreate_String_Succeeds()
    {
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From("hello"), out var key));
        Assert.Equal(FilterValueKind.String, key.Kind);
        Assert.Equal("hello", key.String);
    }

    [Fact] public void FilterIndexValue_TryCreate_Null_ReturnsFalse() =>
        Assert.False(FilterIndexValue.TryCreate(FilterValue.Null, out _));

    [Fact] public void FilterIndexValue_TryCreate_Decimal_ReturnsFalse() =>
        Assert.False(FilterIndexValue.TryCreate(new FilterValue { Kind = FilterValueKind.Decimal, Decimal = 1.5m }, out _));

    [Fact]
    public void FilterIndexValue_TryCreate_UnsignedInteger_Succeeds()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(big), out var key));
        Assert.Equal(FilterValueKind.UnsignedInteger, key.Kind);
        Assert.Equal(big, key.UnsignedInteger);
    }

    [Fact]
    public void FilterIndexValue_TryCreate_Guid_Succeeds()
    {
        var g = Guid.NewGuid();
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(g), out var key));
        Assert.Equal(FilterValueKind.Guid, key.Kind);
        Assert.Equal(g, key.Guid);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Bool_Succeeds()
    {
        Assert.True(FilterIndexValue.TryCreateActual(true, out var key));
        Assert.Equal(FilterValueKind.Boolean, key.Kind);
        Assert.True(key.Boolean);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Byte_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual((byte)10, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
        Assert.Equal(10L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_SByte_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual((sbyte)-3, out var key));
        Assert.Equal(-3L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Short_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual((short)1000, out var key));
        Assert.Equal(1000L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_UShort_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual((ushort)2000, out var key));
        Assert.Equal(2000L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Int_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual(42, out var key));
        Assert.Equal(42L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_UInt_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual(99u, out var key));
        Assert.Equal(99L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Long_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual(123L, out var key));
        Assert.Equal(123L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Float_ProducesNumber()
    {
        Assert.True(FilterIndexValue.TryCreateActual(1.5f, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Double_ProducesNumber()
    {
        Assert.True(FilterIndexValue.TryCreateActual(3.14, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Decimal_ProducesNumber()
    {
        Assert.True(FilterIndexValue.TryCreateActual(1.5m, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_String_ProducesString()
    {
        Assert.True(FilterIndexValue.TryCreateActual("test", out var key));
        Assert.Equal(FilterValueKind.String, key.Kind);
        Assert.Equal("test", key.String);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Null_ReturnsFalse() =>
        Assert.False(FilterIndexValue.TryCreateActual(null, out _));

    [Fact] public void FilterIndexValue_TryCreateActual_UnsupportedType_ReturnsFalse() =>
        Assert.False(FilterIndexValue.TryCreateActual(new object(), out _));

    [Fact] public void FilterIndexValue_TryCreateActual_Enum_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual(TestStatus.Active, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
        Assert.Equal(1L, key.Integer);
    }

    [Fact]
    public void FilterIndexValue_TryCreateActual_ULong_WithinLongMax_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual(500UL, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
        Assert.Equal(500L, key.Integer);
    }

    [Fact]
    public void FilterIndexValue_TryCreateActual_ULong_BeyondLongMax_ProducesUnsignedInteger()
    {
        ulong big = (ulong)long.MaxValue + 10UL;
        Assert.True(FilterIndexValue.TryCreateActual(big, out var key));
        Assert.Equal(FilterValueKind.UnsignedInteger, key.Kind);
        Assert.Equal(big, key.UnsignedInteger);
    }

    [Fact]
    public void FilterIndexValue_TryCreateActual_Guid_ProducesGuid()
    {
        var g = Guid.NewGuid();
        Assert.True(FilterIndexValue.TryCreateActual(g, out var key));
        Assert.Equal(FilterValueKind.Guid, key.Kind);
        Assert.Equal(g, key.Guid);
    }
}

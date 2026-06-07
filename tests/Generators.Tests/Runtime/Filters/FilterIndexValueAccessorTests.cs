using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterIndexValueAccessorTests
{
    [Theory]
    [InlineData(typeof(byte), (byte)42)]
    [InlineData(typeof(sbyte), (sbyte)-1)]
    [InlineData(typeof(short), (short)100)]
    [InlineData(typeof(ushort), (ushort)200)]
    [InlineData(typeof(int), 300)]
    [InlineData(typeof(uint), 400u)]
    public void TryCreateActual_SmallIntegerTypes(Type _, object value)
    {
        Assert.True(FilterIndexValue.TryCreateActual(value, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
    }

    [Fact]
    public void TryCreateActual_Long()
    {
        Assert.True(FilterIndexValue.TryCreateActual(42L, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
        Assert.Equal(42L, key.Integer);
    }

    [Fact]
    public void TryCreateActual_ULongWithinLongRange()
    {
        Assert.True(FilterIndexValue.TryCreateActual(42UL, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
    }

    [Fact]
    public void TryCreateActual_ULongBeyondLongRange()
    {
        ulong big = (ulong)long.MaxValue + 1;
        Assert.True(FilterIndexValue.TryCreateActual(big, out var key));
        Assert.Equal(FilterValueKind.UnsignedInteger, key.Kind);
        Assert.Equal(big, key.UnsignedInteger);
    }

    [Fact]
    public void TryCreateActual_Float()
    {
        Assert.True(FilterIndexValue.TryCreateActual(1.5f, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact]
    public void TryCreateActual_Double()
    {
        Assert.True(FilterIndexValue.TryCreateActual(3.14, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact]
    public void TryCreateActual_Decimal()
    {
        Assert.True(FilterIndexValue.TryCreateActual(9.99m, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact]
    public void TryCreateActual_String()
    {
        Assert.True(FilterIndexValue.TryCreateActual("hello", out var key));
        Assert.Equal(FilterValueKind.String, key.Kind);
        Assert.Equal("hello", key.String);
    }

    [Fact]
    public void TryCreateActual_Guid()
    {
        var g = Guid.NewGuid();
        Assert.True(FilterIndexValue.TryCreateActual(g, out var key));
        Assert.Equal(FilterValueKind.Guid, key.Kind);
        Assert.Equal(g, key.Guid);
    }

    [Fact]
    public void TryCreateActual_Bool()
    {
        Assert.True(FilterIndexValue.TryCreateActual(true, out var key));
        Assert.Equal(FilterValueKind.Boolean, key.Kind);
        Assert.True(key.Boolean);
    }

    [Fact]
    public void TryCreateActual_Enum()
    {
        Assert.True(FilterIndexValue.TryCreateActual(TestKind.B, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
    }

    [Fact]
    public void TryCreateActual_NullReturnsFalse()
    {
        Assert.False(FilterIndexValue.TryCreateActual(null, out _));
    }

    [Fact]
    public void TryCreateActual_UnsupportedTypeReturnsFalse()
    {
        Assert.False(FilterIndexValue.TryCreateActual(DateTime.Now, out _));
    }

    [Fact]
    public void TryCreate_AllValueKinds()
    {
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(true), out var k1));
        Assert.Equal(FilterValueKind.Boolean, k1.Kind);

        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(42L), out var k2));
        Assert.Equal(FilterValueKind.Integer, k2.Kind);

        ulong bigUnsigned = (ulong)long.MaxValue + 1;
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(bigUnsigned), out var k3));
        Assert.Equal(FilterValueKind.UnsignedInteger, k3.Kind);

        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(3.14), out var k4));
        Assert.Equal(FilterValueKind.Number, k4.Kind);

        Assert.True(FilterIndexValue.TryCreate(FilterValue.From("test"), out var k5));
        Assert.Equal(FilterValueKind.String, k5.Kind);

        var g = Guid.NewGuid();
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(g), out var k6));
        Assert.Equal(FilterValueKind.Guid, k6.Kind);
    }

    [Fact]
    public void TryCreate_NullValueReturnsFalse()
    {
        Assert.False(FilterIndexValue.TryCreate(FilterValue.Null, out _));
    }

    [Fact]
    public void TryCreateActual_WithAccessor_BooleanRequired()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Boolean,
            requiredBoolean: obj => ((BoolSubject)obj).Active);
        Assert.True(FilterIndexValue.TryCreateActual(accessor, new BoolSubject(true), out var key));
        Assert.True(key.Boolean);
    }

    [Fact]
    public void TryCreateActual_WithAccessor_NumberRequired()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Number,
            requiredNumber: obj => ((NumSubject)obj).Value);
        Assert.True(FilterIndexValue.TryCreateActual(accessor, new NumSubject(5.0), out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact]
    public void TryCreateActual_WithAccessor_String()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.String,
            text: obj => ((StrSubject)obj).Name);
        Assert.True(FilterIndexValue.TryCreateActual(accessor, new StrSubject("hi"), out var key));
        Assert.Equal("hi", key.String);
    }

    [Fact]
    public void TryCreateActual_WithAccessor_StringNull()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.String,
            text: _ => null);
        Assert.False(FilterIndexValue.TryCreateActual(accessor, new StrSubject(""), out _));
    }

    [Fact]
    public void TryCreateActual_WithAccessor_GuidRequired()
    {
        var g = Guid.NewGuid();
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Guid,
            requiredGuid: _ => g);
        Assert.True(FilterIndexValue.TryCreateActual(accessor, new object(), out var key));
        Assert.Equal(g, key.Guid);
    }

    [Fact]
    public void TryCreateActual_WithAccessor_EnumRequired()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Enum,
            requiredEnumeration: _ => 42L);
        Assert.True(FilterIndexValue.TryCreateActual(accessor, new object(), out var key));
        Assert.Equal(42L, key.Integer);
    }

    [Fact]
    public void TryCreateActual_WithAccessor_BooleanNullable()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Boolean,
            boolean: _ => null);
        Assert.False(FilterIndexValue.TryCreateActual(accessor, new object(), out _));
    }

    [Fact]
    public void TryCreateActual_WithAccessor_NumberNullable()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Number,
            number: _ => null);
        Assert.False(FilterIndexValue.TryCreateActual(accessor, new object(), out _));
    }

    [Fact]
    public void TryCreateActual_WithAccessor_GuidNullable()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Guid,
            guid: _ => null);
        Assert.False(FilterIndexValue.TryCreateActual(accessor, new object(), out _));
    }

    [Fact]
    public void TryCreateActual_WithAccessor_EnumNullable()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Enum,
            enumeration: _ => null);
        Assert.False(FilterIndexValue.TryCreateActual(accessor, new object(), out _));
    }

    private sealed record BoolSubject(bool Active);
    private sealed record NumSubject(double Value);
    private sealed record StrSubject(string? Name);

    internal enum TestKind { A, B, C }
}

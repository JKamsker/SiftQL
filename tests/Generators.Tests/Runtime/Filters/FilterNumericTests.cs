using SiftQL.Expressions;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterNumericTests
{
    [Theory]
    [InlineData(typeof(sbyte), true)]
    [InlineData(typeof(short), true)]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(long), true)]
    [InlineData(typeof(string), false)]
    public void IsSignedIntegral_ReturnsExpected(Type type, bool expected)
    {
        Assert.Equal(expected, FilterNumeric.IsSignedIntegral(type));
    }

    [Theory]
    [InlineData(typeof(byte), true)]
    [InlineData(typeof(ushort), true)]
    [InlineData(typeof(uint), true)]
    [InlineData(typeof(ulong), true)]
    [InlineData(typeof(int), false)]
    public void IsUnsignedIntegral_ReturnsExpected(Type type, bool expected)
    {
        Assert.Equal(expected, FilterNumeric.IsUnsignedIntegral(type));
    }

    [Theory]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(ulong), true)]
    [InlineData(typeof(decimal), true)]
    [InlineData(typeof(double), false)]
    [InlineData(typeof(float), false)]
    public void IsExactNumeric_ReturnsExpected(Type type, bool expected)
    {
        Assert.Equal(expected, FilterNumeric.IsExactNumeric(type));
    }

    [Fact]
    public void TryDoubleToDecimal_NaN()
    {
        Assert.False(FilterNumeric.TryDoubleToDecimal(double.NaN, out _));
    }

    [Fact]
    public void TryDoubleToDecimal_Infinity()
    {
        Assert.False(FilterNumeric.TryDoubleToDecimal(double.PositiveInfinity, out _));
    }

    [Fact]
    public void TryDoubleToDecimal_Overflow()
    {
        Assert.False(FilterNumeric.TryDoubleToDecimal(double.MaxValue, out _));
    }

    [Fact]
    public void TryDoubleToDecimal_Normal()
    {
        Assert.True(FilterNumeric.TryDoubleToDecimal(1.5, out decimal result));
        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void TryNumberDecimal_Integer()
    {
        Assert.True(FilterNumeric.TryNumberDecimal(FilterValue.From(42L), out decimal result));
        Assert.Equal(42m, result);
    }

    [Fact]
    public void TryNumberDecimal_UnsignedInteger()
    {
        Assert.True(FilterNumeric.TryNumberDecimal(FilterValue.From(42UL), out decimal result));
        Assert.Equal(42m, result);
    }

    [Fact]
    public void TryNumberDecimal_Decimal()
    {
        Assert.True(FilterNumeric.TryNumberDecimal(FilterValue.From(1.5m), out decimal result));
        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void TryNumberDecimal_Number()
    {
        Assert.True(FilterNumeric.TryNumberDecimal(FilterValue.From(3.14), out decimal result));
        Assert.Equal((decimal)3.14, result);
    }

    [Fact]
    public void TryNumberDecimal_StringReturnsFalse()
    {
        Assert.False(FilterNumeric.TryNumberDecimal(FilterValue.From("text"), out _));
    }

    [Fact]
    public void TryDoubleToUInt64_Negative()
    {
        Assert.False(FilterNumeric.TryDoubleToUInt64(-1.0, out _));
    }

    [Fact]
    public void TryDoubleToUInt64_Fractional()
    {
        Assert.False(FilterNumeric.TryDoubleToUInt64(1.5, out _));
    }

    [Fact]
    public void TryDoubleToUInt64_Valid()
    {
        Assert.True(FilterNumeric.TryDoubleToUInt64(42.0, out ulong result));
        Assert.Equal(42UL, result);
    }

    [Fact]
    public void TryDoubleToInt64_Fractional()
    {
        Assert.False(FilterNumeric.TryDoubleToInt64(1.5, out _));
    }

    [Fact]
    public void TryDoubleToInt64_Valid()
    {
        Assert.True(FilterNumeric.TryDoubleToInt64(42.0, out long result));
        Assert.Equal(42L, result);
    }

    [Fact]
    public void TryExactDecimal_AllIntTypes()
    {
        Assert.True(FilterNumeric.TryExactDecimal((byte)1, out _));
        Assert.True(FilterNumeric.TryExactDecimal((sbyte)-1, out _));
        Assert.True(FilterNumeric.TryExactDecimal((short)100, out _));
        Assert.True(FilterNumeric.TryExactDecimal((ushort)200, out _));
        Assert.True(FilterNumeric.TryExactDecimal(300, out _));
        Assert.True(FilterNumeric.TryExactDecimal(400u, out _));
        Assert.True(FilterNumeric.TryExactDecimal(500L, out _));
        Assert.True(FilterNumeric.TryExactDecimal(600UL, out _));
        Assert.True(FilterNumeric.TryExactDecimal(1.5m, out var r));
        Assert.Equal(1.5m, r);
    }

    [Fact]
    public void TryExactDecimal_UnsupportedType()
    {
        Assert.False(FilterNumeric.TryExactDecimal(1.5, out _));
        Assert.False(FilterNumeric.TryExactDecimal("nope", out _));
        Assert.False(FilterNumeric.TryExactDecimal(null, out _));
    }
}

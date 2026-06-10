using SiftQL.Expressions;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterValueCreationTests
{
    [Fact]
    public void FilterValue_FromObject_Null()
    {
        FilterValue value = FilterValue.FromObject(null);
        Assert.Equal(FilterValueKind.Null, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_Bool()
    {
        FilterValue value = FilterValue.FromObject(true);
        Assert.Equal(FilterValueKind.Boolean, value.Kind);
        Assert.True(value.Boolean);
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((sbyte)-1)]
    [InlineData((short)100)]
    [InlineData((ushort)200)]
    [InlineData(42)]
    [InlineData(42u)]
    [InlineData(42L)]
    public void FilterValue_FromObject_IntegerTypes(object val)
    {
        FilterValue value = FilterValue.FromObject(val);
        Assert.Equal(FilterValueKind.Integer, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_ULong_Large()
    {
        ulong large = (ulong)long.MaxValue + 1;
        FilterValue value = FilterValue.FromObject(large);
        Assert.Equal(FilterValueKind.UnsignedInteger, value.Kind);
        Assert.Equal(large, value.UnsignedInteger);
    }

    [Fact]
    public void FilterValue_FromObject_Float()
    {
        FilterValue value = FilterValue.FromObject(1.5f);
        Assert.Equal(FilterValueKind.Number, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_Double()
    {
        FilterValue value = FilterValue.FromObject(2.5);
        Assert.Equal(FilterValueKind.Number, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_Decimal_Fractional()
    {
        FilterValue value = FilterValue.FromObject(99.99m);
        Assert.Equal(FilterValueKind.Decimal, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_Decimal_Integral()
    {
        FilterValue value = FilterValue.FromObject(42m);
        Assert.Equal(FilterValueKind.Integer, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_String()
    {
        FilterValue value = FilterValue.FromObject("hello");
        Assert.Equal(FilterValueKind.String, value.Kind);
        Assert.Equal("hello", value.String);
    }

    [Fact]
    public void FilterValue_FromObject_Guid()
    {
        var guid = Guid.NewGuid();
        FilterValue value = FilterValue.FromObject(guid);
        Assert.Equal(FilterValueKind.Guid, value.Kind);
        Assert.Equal(guid, value.Guid);
    }

    [Fact]
    public void FilterValue_FromObject_Enum_ReturnsString()
    {
        FilterValue value = FilterValue.FromObject(DayOfWeek.Monday);
        Assert.Equal(FilterValueKind.String, value.Kind);
        Assert.Equal("Monday", value.String);
    }

    [Fact]
    public void FilterValue_FromObject_UnsupportedType_Throws()
    {
        Assert.Throws<KernelExpressionException>(() => FilterValue.FromObject(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void FilterValue_From_ULong_Small_ReturnsInteger()
    {
        FilterValue value = FilterValue.From(100UL);
        Assert.Equal(FilterValueKind.Integer, value.Kind);
        Assert.Equal(100, value.Integer);
    }

    [Fact]
    public void FilterValue_From_Decimal_WholeNumber_ReturnsInteger()
    {
        FilterValue value = FilterValue.From(7m);
        Assert.Equal(FilterValueKind.Integer, value.Kind);
        Assert.Equal(7, value.Integer);
    }
}

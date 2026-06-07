using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterCompilerEndToEndTests
{
    [Fact]
    public void InFilter_WithBooleanValues()
    {
        var filter = FilterExpression.In(
            nameof(BoolFilterSubject.Active),
            [FilterValue.From(true)]);
        var kernel = FilterCompiler.Compile(typeof(BoolFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new BoolFilterSubject(true)));
        Assert.False(kernel.Matches(new BoolFilterSubject(false)));
    }

    [Fact]
    public void InFilter_WithGuidValues()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var filter = FilterExpression.In(
            nameof(GuidFilterSubject.Token),
            [FilterValue.From(g1), FilterValue.From(g2)]);
        var kernel = FilterCompiler.Compile(typeof(GuidFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new GuidFilterSubject(g1)));
        Assert.True(kernel.Matches(new GuidFilterSubject(g2)));
        Assert.False(kernel.Matches(new GuidFilterSubject(Guid.Empty)));
    }

    [Fact]
    public void InFilter_WithStringAndNull()
    {
        var filter = FilterExpression.In(
            nameof(StrFilterSubject.Name),
            [FilterValue.From("hello"), FilterValue.Null]);
        var kernel = FilterCompiler.Compile(typeof(StrFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new StrFilterSubject("hello")));
        Assert.True(kernel.Matches(new StrFilterSubject(null)));
        Assert.False(kernel.Matches(new StrFilterSubject("world")));
    }

    [Fact]
    public void InFilter_WithLargeStringSet()
    {
        var values = Enumerable.Range(0, 5).Select(i => FilterValue.From($"val{i}")).ToArray();
        var filter = FilterExpression.In(nameof(StrFilterSubject.Name), values);
        var kernel = FilterCompiler.Compile(typeof(StrFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new StrFilterSubject("val3")));
        Assert.False(kernel.Matches(new StrFilterSubject("other")));
    }

    [Fact]
    public void InFilter_WithUnsignedIntegerValues()
    {
        var filter = FilterExpression.In(
            nameof(UIntFilterSubject.Value),
            [FilterValue.From(10L), FilterValue.From(20L)]);
        var kernel = FilterCompiler.Compile(typeof(UIntFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new UIntFilterSubject(10)));
        Assert.False(kernel.Matches(new UIntFilterSubject(30)));
    }

    [Fact]
    public void CompareFilter_UnsignedIntegerNegativeValue()
    {
        var filter = FilterExpression.Compare(
            nameof(UIntFilterSubject.Value),
            FilterOperator.Equal,
            FilterValue.From(-1L));
        var kernel = FilterCompiler.Compile(typeof(UIntFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.False(kernel.Matches(new UIntFilterSubject(0)));
    }

    [Fact]
    public void CompareFilter_UnsignedIntegerGreaterThanNegative()
    {
        var filter = FilterExpression.Compare(
            nameof(UIntFilterSubject.Value),
            FilterOperator.GreaterThan,
            FilterValue.From(-1L));
        var kernel = FilterCompiler.Compile(typeof(UIntFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new UIntFilterSubject(0)));
    }

    [Fact]
    public void CompareFilter_DecimalWithIntegerValue()
    {
        var filter = FilterExpression.Compare(
            nameof(DecFilterSubject.Amount),
            FilterOperator.Equal,
            FilterValue.From(42L));
        var kernel = FilterCompiler.Compile(typeof(DecFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new DecFilterSubject(42m)));
        Assert.False(kernel.Matches(new DecFilterSubject(43m)));
    }

    [Fact]
    public void CompareFilter_UnsignedIntegerUnsignedValue()
    {
        var filter = FilterExpression.Compare(
            nameof(UIntFilterSubject.Value),
            FilterOperator.Equal,
            FilterValue.From(10UL));
        var kernel = FilterCompiler.Compile(typeof(UIntFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new UIntFilterSubject(10)));
    }

    [Fact]
    public void CompareFilter_SignedWithLargeUnsigned()
    {
        ulong big = (ulong)long.MaxValue + 1;
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(big));
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Immediate);
        Assert.False(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    [Fact]
    public void CompareFilter_SignedWithLargeUnsigned_LessThan()
    {
        ulong big = (ulong)long.MaxValue + 1;
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.LessThan,
            FilterValue.From(big));
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    [Fact]
    public void CompareFilter_DecimalWithUnsigned()
    {
        var filter = FilterExpression.Compare(
            nameof(DecFilterSubject.Amount),
            FilterOperator.Equal,
            FilterValue.From(42UL));
        var kernel = FilterCompiler.Compile(typeof(DecFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new DecFilterSubject(42m)));
    }

    [Fact]
    public void CompareFilter_IntWithDecimalValue()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(42.0m));
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    [Fact]
    public void CompareFilter_IntWithDoubleValue()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(42.0));
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    [Fact]
    public void CompareFilter_IntWithNaNDouble()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(double.NaN));
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Immediate);
        Assert.False(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    private sealed record BoolFilterSubject(bool Active) : IFilterSubject;
    private sealed record GuidFilterSubject(Guid Token) : IFilterSubject;
    private sealed record StrFilterSubject(string? Name) : IFilterSubject;
    private sealed record UIntFilterSubject(uint Value) : IFilterSubject;
    private sealed record DecFilterSubject(decimal Amount) : IFilterSubject;
}

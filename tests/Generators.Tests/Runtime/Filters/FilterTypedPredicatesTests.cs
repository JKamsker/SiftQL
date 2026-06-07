using SiftQL.Expressions;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterTypedPredicatesTests
{
    [Fact]
    public void CompileCompare_NumberNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 5.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberNotEqual()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 5.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberGreaterThan()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 10.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(5.0), FilterOperator.GreaterThan);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberGreaterThanOrEqual()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 5.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(5.0), FilterOperator.GreaterThanOrEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberLessThan()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 3.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(5.0), FilterOperator.LessThan);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberLessThanOrEqual()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 5.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(5.0), FilterOperator.LessThanOrEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberIntegerValue()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 42.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(42L), FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberUnsignedIntegerValue()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 42.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(42UL), FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberDecimalValue()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 1.5);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(1.5m), FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_BooleanNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Boolean, boolean: _ => true);
        var field = CreateField("Active", typeof(bool), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompileCompare_BooleanNotEqualNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Boolean, boolean: _ => true);
        var field = CreateField("Active", typeof(bool), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_GuidNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Guid, guid: _ => Guid.NewGuid());
        var field = CreateField("Token", typeof(Guid), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompileCompare_GuidNotEqualNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Guid, guid: _ => Guid.NewGuid());
        var field = CreateField("Token", typeof(Guid), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_GuidNotEqual()
    {
        var g = Guid.NewGuid();
        var scalar = new FilterScalarAccessor(FilterScalarKind.Guid, guid: _ => g);
        var field = CreateField("Token", typeof(Guid), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(Guid.Empty), FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_EnumNonIntegerReturnsNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Enum, enumeration: _ => 0L);
        var field = CreateField("Kind", typeof(TestKind), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From("A"), FilterOperator.Equal);
        Assert.Null(pred);
    }

    [Fact]
    public void CompileCompare_EnumNotEqual()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Enum, enumeration: _ => 0L);
        var field = CreateField("Kind", typeof(TestKind), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(1L), FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_StringNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.String, text: _ => "hello");
        var field = CreateField("Name", typeof(string), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompileCompare_StringNotEqual()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.String, text: _ => "a");
        var field = CreateField("Name", typeof(string), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From("b"), FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NullScalarReturnsNull()
    {
        var field = CreateField("Name", typeof(string), null);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From("a"), FilterOperator.Equal);
        Assert.Null(pred);
    }

    [Fact]
    public void CompileIn_EnumWithStringReturnsNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Enum, enumeration: _ => 0L);
        var field = CreateField("Kind", typeof(TestKind), scalar);
        var pred = FilterTypedPredicates.TryCompileIn(field, [FilterValue.From("A")]);
        Assert.Null(pred);
    }

    [Fact]
    public void CompileIn_NullScalarReturnsNull()
    {
        var field = CreateField("Name", typeof(string), null);
        var pred = FilterTypedPredicates.TryCompileIn(field, [FilterValue.From("a")]);
        Assert.Null(pred);
    }

    [Fact]
    public void CompileCompare_NumberNotDoubleTypeReturnsNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 42.0);
        var field = CreateField("Amount", typeof(decimal), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(42L), FilterOperator.Equal);
        Assert.Null(pred);
    }

    [Fact]
    public void CompileIn_NumberNotDoubleTypeReturnsNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 42.0);
        var field = CreateField("Amount", typeof(decimal), scalar);
        var pred = FilterTypedPredicates.TryCompileIn(field, [FilterValue.From(42L)]);
        Assert.Null(pred);
    }

    private static FilterField CreateField(string name, Type valueType, FilterScalarAccessor? scalar)
    {
        return new FilterField(
            name,
            valueType,
            FilterFieldKind.Scalar,
            _ => null,
            ScalarAccessor: scalar,
            ArrayAccessor: null);
    }

    internal enum TestKind { A, B, C }
}

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class Wave1BugRegressionTests
{
    [Fact]
    public void ContainsFallbackReturnsEarlyOnFirstMatch()
    {
        var items = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        bool result = FilterValues.Contains(items, FilterValue.From(1L));
        Assert.True(result);
    }

    [Fact]
    public void ContainsBoundedCollectionReturnsAfterFirstMatch()
    {
        var items = new ThrowAfterFirstMatchCollection();

        bool result = FilterValues.Contains(items, FilterValue.From(42L));

        Assert.True(result);
    }

    [Fact]
    public void ContainsFallbackReturnsFalseWhenNoMatch()
    {
        var items = new List<int> { 1, 2, 3 };
        Assert.False(FilterValues.Contains(items, FilterValue.From(99L)));
    }

    [Fact]
    public void ContainsSingleFloatWidensCorrectly()
    {
        Assert.True(FilterArrayContains.ContainsSingle([1.1f], 1.1f));
        Assert.True(FilterArrayContains.ContainsSingle([0.3f], 0.3f));
        Assert.True(FilterArrayContains.ContainsSingle([42.0f], 42.0));
    }

    [Fact]
    public void ContainsDecimalFallbackUsesExactComparison()
    {
        Assert.True(FilterArrayContains.ContainsDecimalValue([0.1m, 0.2m, 0.3m], 0.1m));
        Assert.False(FilterArrayContains.ContainsDecimalValue([0.1m, 0.2m], 0.4m));
    }

    [Fact]
    public void EnumFieldWithNonIntegerValueFallsBackGracefully()
    {
        var filter = FilterExpression.Compare(
            nameof(EnumSubject.Status),
            FilterOperator.Equal,
            FilterValue.From("Active"));

        var kernel = FilterCompiler.Compile(
            typeof(EnumSubject),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new EnumSubject(TestStatus.Active)));
        Assert.False(kernel.Matches(new EnumSubject(TestStatus.Inactive)));
    }

    [Fact]
    public void EnumFieldWithNonIntegerValueFallsBackGracefullyTiered()
    {
        var filter = FilterExpression.Compare(
            nameof(EnumSubject.Status),
            FilterOperator.Equal,
            FilterValue.From("Active"));

        var kernel = FilterCompiler.Compile(
            typeof(EnumSubject),
            filter,
            FilterCompilerOptions.Tiered);

        Assert.True(kernel.Matches(new EnumSubject(TestStatus.Active)));
        Assert.False(kernel.Matches(new EnumSubject(TestStatus.Inactive)));
    }

    [Fact]
    public void EnumInFilterWithStringValuesFallsBackGracefully()
    {
        var filter = FilterExpression.In(
            nameof(EnumSubject.Status),
            [FilterValue.From("Active"), FilterValue.From("Inactive")]);

        var kernel = FilterCompiler.Compile(
            typeof(EnumSubject),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new EnumSubject(TestStatus.Active)));
        Assert.True(kernel.Matches(new EnumSubject(TestStatus.Inactive)));
    }

    [Fact]
    public void EnumInFilterWithStringValuesFallsBackGracefullyTiered()
    {
        var filter = FilterExpression.In(
            nameof(EnumSubject.Status),
            [FilterValue.From("Active"), FilterValue.From("Inactive")]);

        var kernel = FilterCompiler.Compile(
            typeof(EnumSubject),
            filter,
            FilterCompilerOptions.Tiered);

        Assert.True(kernel.Matches(new EnumSubject(TestStatus.Active)));
        Assert.True(kernel.Matches(new EnumSubject(TestStatus.Inactive)));
    }

    [Fact]
    public void FilterValuesContainsNullArrayReturnsFalse()
    {
        Assert.False(FilterArrayContains.ContainsInt32(null, 1.0));
        Assert.False(FilterArrayContains.ContainsString(null, "x"));
        Assert.False(FilterArrayContains.ContainsGuid(null, Guid.Empty));
    }

    [Fact]
    public void FilterValuesContainsOversizedArrayThrows()
    {
        var oversized = new int[257];
        Assert.Throws<InvalidOperationException>(() =>
            FilterArrayContains.ContainsInt32(oversized, 0.0));
    }

    [Fact]
    public void FilterValuesContainsStringReturnsFalse()
    {
        Assert.False(FilterValues.Contains("hello", FilterValue.From("h")));
    }

    private sealed record EnumSubject(TestStatus Status) : IFilterSubject;

    public enum TestStatus
    {
        Active = 1,
        Inactive = 2,
    }

    private sealed class ThrowAfterFirstMatchCollection : System.Collections.ICollection
    {
        public int Count => 2;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        public void CopyTo(Array array, int index) =>
            throw new NotSupportedException();

        public System.Collections.IEnumerator GetEnumerator()
        {
            yield return 42;
            throw new InvalidOperationException("Contains should stop after the first match.");
        }
    }
}

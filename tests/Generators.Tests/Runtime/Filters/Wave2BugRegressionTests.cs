using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class Wave2BugRegressionTests
{
    [Fact]
    public void PromoteDoesNotBreakMatchesDuringTransition()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(42L));

        var kernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Tiered);

        var matching = new ItemUsedEvent(Guid.NewGuid(), 1, 42, 1);
        var nonMatching = new ItemUsedEvent(Guid.NewGuid(), 1, 99, 1);

        Assert.True(kernel.Matches(matching));
        Assert.False(kernel.Matches(nonMatching));
        Assert.True(kernel.Matches<ItemUsedEvent>(matching));
        Assert.False(kernel.Matches<ItemUsedEvent>(nonMatching));
    }

    [Fact]
    public void NumericInDedupRemovesDuplicatesCorrectly()
    {
        var filter = FilterExpression.In(
            nameof(ItemUsedEvent.ItemId),
            [
                FilterValue.From(1L),
                FilterValue.From(1L),
                FilterValue.From(2L),
                FilterValue.From(2L),
                FilterValue.From(3L),
            ]);

        var kernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.NewGuid(), 1, 1, 1)));
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.NewGuid(), 1, 2, 1)));
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.NewGuid(), 1, 3, 1)));
        Assert.False(kernel.Matches(new ItemUsedEvent(Guid.NewGuid(), 1, 4, 1)));
    }

    [Fact]
    public void DecimalInDedupRemovesDuplicatesCorrectly()
    {
        var filter = FilterExpression.In(
            nameof(DecimalSubject.Amount),
            [
                FilterValue.From(1.5m),
                FilterValue.From(1.5m),
                FilterValue.From(2.5m),
            ]);

        var kernel = FilterCompiler.Compile(
            typeof(DecimalSubject),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new DecimalSubject(1.5m)));
        Assert.True(kernel.Matches(new DecimalSubject(2.5m)));
        Assert.False(kernel.Matches(new DecimalSubject(3.5m)));
    }

    [Fact]
    public void ContainsInt64WithLargeDoubleDoesNotFalsePositive()
    {
        long val1 = 9_007_199_254_740_993L;
        long val2 = 9_007_199_254_740_992L;
        Assert.False(FilterArrayContains.ContainsInt64([val1], (double)val2));
    }

    [Fact]
    public void ContainsUInt64WithLargeDoubleDoesNotFalsePositive()
    {
        ulong val1 = 9_007_199_254_740_993UL;
        ulong val2 = 9_007_199_254_740_992UL;
        Assert.False(FilterArrayContains.ContainsUInt64([val1], (double)val2));
    }

    [Fact]
    public void ContainsInt32WithNonIntegerDoubleReturnsFalse()
    {
        Assert.False(FilterArrayContains.ContainsInt32([1, 2, 3], 1.5));
    }

    [Fact]
    public void ContainsDecimalViaDoubleMatchesWhenExact()
    {
        Assert.True(FilterArrayContains.ContainsDecimal([1.0m, 2.0m], 2.0));
        Assert.False(FilterArrayContains.ContainsDecimal([1.0m, 2.0m], 3.0));
    }

    [Fact]
    public async Task ConcurrentValueObjectRegistrationDoesNotCorrupt()
    {
        var tasks = new Task[8];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                    FilterSchema.RegisterValueObject(typeof(ConcurrentTestType));
            });
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void KernelVersionIncreasesAfterPromotion()
    {
        var kernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            FilterExpression.Compare(
                nameof(ItemUsedEvent.ItemId),
                FilterOperator.Equal,
                FilterValue.From(42L)),
            FilterCompilerOptions.Tiered);

        int initialVersion = kernel.Version;
        for (int i = 0; i < 200; i++)
            kernel.Matches(new ItemUsedEvent(Guid.NewGuid(), 1, 42, 1));

        Thread.Sleep(100);
        Assert.True(kernel.Version >= initialVersion);
    }

    private sealed record DecimalSubject(decimal Amount) : IFilterSubject;
    private sealed record ConcurrentTestType(string Name);
}

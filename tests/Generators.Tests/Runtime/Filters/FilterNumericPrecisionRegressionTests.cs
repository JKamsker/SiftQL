using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterNumericPrecisionRegressionTests
{
    private const long RoundedInteger = 9_007_199_254_740_992L;
    private const long NeighborInteger = 9_007_199_254_740_993L;

    [Fact]
    public void DecimalLiteralsUseNumericFastPaths()
    {
        var compare = FilterCompiler.Compile(
            typeof(DecimalFastPathSubject),
            FilterExpression.Compare(
                nameof(DecimalFastPathSubject.Score),
                FilterOperator.GreaterThan,
                FilterValue.From(1.25m)),
            FilterCompilerOptions.Immediate);
        var inFilter = FilterCompiler.Compile(
            typeof(DecimalFastPathSubject),
            FilterExpression.In(
                nameof(DecimalFastPathSubject.Score),
                [FilterValue.From(1.25m)]),
            FilterCompilerOptions.Immediate);
        var contains = FilterCompiler.Compile(
            typeof(DecimalFastPathSubject),
            FilterExpression.Contains(
                nameof(DecimalFastPathSubject.Scores),
                FilterValue.From(1.25m)),
            FilterCompilerOptions.Immediate);

        Assert.False(compare.Matches(new DecimalFastPathSubject(1.0, [])));
        Assert.True(compare.Matches(new DecimalFastPathSubject(2.0, [])));
        Assert.True(inFilter.Matches(new DecimalFastPathSubject(1.25, [])));
        Assert.True(contains.Matches(new DecimalFastPathSubject(0, [1.25])));
    }

    [Fact]
    public void IntegralScalarDecimalDoesNotUseRoundedDoubleFastPath()
    {
        var compare = FilterExpression.Compare(
            nameof(IntegralSubject.Count),
            FilterOperator.Equal,
            FilterValue.From(1.0000000000000000000000000001m));
        var inFilter = FilterExpression.In(
            nameof(IntegralSubject.Count),
            [FilterValue.From(1.0000000000000000000000000001m)]);

        AssertFilter(compare, new FilterCase<IntegralSubject>(new(1), false));
        AssertFilter(inFilter, new FilterCase<IntegralSubject>(new(1), false));
    }

    [Fact]
    public void RoundedIntegerNeighborFiltersUseExactSemantics()
    {
        NumericSubject subject = Subject();

        AssertFilter(
            FilterExpression.Compare(
                nameof(NumericSubject.Amount),
                FilterOperator.Equal,
                FilterValue.From(RoundedInteger) with { ParameterKey = "p0" }),
            new FilterCase<NumericSubject>(subject, false));
        AssertFilter(
            FilterExpression.In(nameof(NumericSubject.Amount), [FilterValue.From(RoundedInteger)]),
            new FilterCase<NumericSubject>(subject, false));
        AssertFilter(
            FilterExpression.Contains(nameof(NumericSubject.LongIds), FilterValue.From(RoundedInteger)),
            new FilterCase<NumericSubject>(subject, false));
        AssertFilter(
            FilterExpression.Contains(nameof(NumericSubject.Amounts), FilterValue.From(RoundedInteger)),
            new FilterCase<NumericSubject>(subject, false));
        AssertFilter(
            FilterExpression.Compare(
                nameof(NumericSubject.Amount),
                FilterOperator.Equal,
                FilterValue.From((double)RoundedInteger)),
            new FilterCase<NumericSubject>(subject, false));
        AssertFilter(
            FilterExpression.In(nameof(NumericSubject.UnsignedId), [FilterValue.From((double)RoundedInteger)]),
            new FilterCase<NumericSubject>(subject, false));
        AssertFilter(
            FilterExpression.Contains(nameof(NumericSubject.LongIds), FilterValue.From((double)RoundedInteger)),
            new FilterCase<NumericSubject>(subject, false));
    }

    [Fact]
    public void NullableValueTypeArraysRemainFilterableThroughFallbackSchema()
    {
        var filter = FilterExpression.Contains(
            nameof(NullableArraySubject.OptionalCounts),
            FilterValue.Null);

        AssertFilter(
            filter,
            new FilterCase<NullableArraySubject>(new([1, null, 3]), true),
            new FilterCase<NullableArraySubject>(new([1, 2, 3]), false));
    }

    [Fact]
    public void NumericInRejectsNaNRegardlessOfLookupThreshold()
    {
        var subject = new FloatingSubject(double.NaN);

        AssertFilter(
            FilterExpression.In(
                nameof(FloatingSubject.Score),
                [FilterValue.From(1D), FilterValue.From(2D), FilterValue.From(3D), FilterValue.From(4D)]),
            new FilterCase<FloatingSubject>(subject, false));
        AssertFilter(
            FilterExpression.In(
                nameof(FloatingSubject.Score),
                [
                    FilterValue.From(1D),
                    FilterValue.From(2D),
                    FilterValue.From(3D),
                    FilterValue.From(4D),
                    FilterValue.From(5D),
                ]),
            new FilterCase<FloatingSubject>(subject, false));
    }

    [Fact]
    public void ExactNumericOrderedNumberFallsBackConsistentlyAcrossModes()
    {
        var filter = FilterExpression.Compare(
            nameof(IntegralSubject.Count),
            FilterOperator.LessThan,
            FilterValue.From(double.MaxValue));

        AssertFilter(filter, new FilterCase<IntegralSubject>(new(10), true));
    }

    [Fact]
    public void UnsignedScalarIndexesMatchUnsignedValues()
    {
        var small = new FilterSubscriptionIndex<string>(typeof(UIntIndexSubject));
        small.Add(
            "small",
            FilterExpression.Compare(
                nameof(UIntIndexSubject.Id),
                FilterOperator.Equal,
                FilterValue.From(42UL)));

        Assert.Contains("small", small.SnapshotCandidates(new UIntIndexSubject(42U)));
        Assert.Empty(small.SnapshotCandidates(new UIntIndexSubject(41U)));

        var large = new FilterSubscriptionIndex<string>(typeof(ULongIndexSubject));
        large.Add(
            "large",
            FilterExpression.Compare(
                nameof(ULongIndexSubject.Id),
                FilterOperator.Equal,
                FilterValue.From(ulong.MaxValue)));

        Assert.Contains("large", large.SnapshotCandidates(new ULongIndexSubject(ulong.MaxValue)));
        Assert.Empty(large.SnapshotCandidates(new ULongIndexSubject(1UL)));
    }

    [Fact]
    public void UlongBackedEnumOverflowFallsBackWithoutDroppingCandidate()
    {
        var filter = FilterExpression.Compare(
            nameof(BigEnumSubject.Kind),
            FilterOperator.Equal,
            FilterValue.From(nameof(BigEnum.Huge)));
        var index = new FilterSubscriptionIndex<string>(typeof(BigEnumSubject));
        index.Add("enum", filter);

        var subject = new BigEnumSubject(BigEnum.Huge);

        Assert.Contains("enum", index.SnapshotCandidates(subject));
        Assert.True(FilterCompiler.Compile(typeof(BigEnumSubject), filter).Matches(subject));
    }

    [Fact]
    public void ProjectedDecimalFieldFiltersThroughDynamicSchema()
    {
        var filter = FilterExpression.Compare(
            ProjectedEventPaths.Field("Amount"),
            FilterOperator.GreaterThan,
            FilterValue.From(1m));
        var kernel = FilterCompiler.CompileWithSchema(
            typeof(ProjectedEvent),
            filter,
            FilterCompilerOptions.Immediate,
            errorFactory: null,
            _ => ProjectedEventFilterSchema.ForFilter(filter));
        var projected = new ProjectedEvent
        {
            Fields = [new ProjectedEventField("Amount", ProjectedEventValue.FromDecimal(1.25m))],
        };

        Assert.True(kernel.Matches(projected));
    }

    [Fact]
    public void ProjectionIncludeRequiredDoubleAcceptsDecimal()
    {
        var include = new EventProjectionInclude(
            "window",
            "result",
            [new EventProjectionArgument("seconds", FilterValue.From(1.5m))]);

        Assert.Equal(1.5D, ProjectionIncludeArguments.RequiredDouble(include, "seconds"));
    }

    [Fact]
    public void UnsignedEnumNumericLiteralDoesNotWrapToOutOfRangeValue()
    {
        bool equal = FilterValues.Compare(
            HugeKind.Last,
            FilterValue.From(-1L),
            FilterOperator.Equal);

        Assert.False(equal);
        Assert.True(FilterValues.Compare(HugeKind.First, FilterValue.From(1L), FilterOperator.Equal));
    }

    [Fact]
    public void UnknownOrderedExpectedKindDoesNotFallBackToNumber()
    {
        var invalid = new FilterValue { Kind = (FilterValueKind)999, Number = 1000D };

        Assert.False(FilterValues.Compare(10, invalid, FilterOperator.LessThan));
    }

    [Fact]
    public void EnumIntegerEqualityCoversAllBackingTypes()
    {
        Assert.True(FilterValues.Compare(ByteKind.Value, FilterValue.From(1L), FilterOperator.Equal));
        Assert.False(FilterValues.Compare(ByteKind.Value, FilterValue.From(2L), FilterOperator.Equal));
        Assert.False(FilterValues.Compare(ByteKind.Value, FilterValue.From(-1L), FilterOperator.Equal));

        Assert.True(FilterValues.Compare(SByteKind.Value, FilterValue.From(1L), FilterOperator.Equal));
        Assert.False(FilterValues.Compare(SByteKind.Value, FilterValue.From(2L), FilterOperator.Equal));
        Assert.True(FilterValues.Compare(SByteKind.Negative, FilterValue.From(-1L), FilterOperator.Equal));

        Assert.True(FilterValues.Compare(ShortKind.Value, FilterValue.From(1L), FilterOperator.Equal));
        Assert.False(FilterValues.Compare(ShortKind.Value, FilterValue.From(2L), FilterOperator.Equal));

        Assert.True(FilterValues.Compare(UShortKind.Value, FilterValue.From(1L), FilterOperator.Equal));
        Assert.False(FilterValues.Compare(UShortKind.Value, FilterValue.From(-1L), FilterOperator.Equal));

        Assert.True(FilterValues.Compare(IntKind.Value, FilterValue.From(1L), FilterOperator.Equal));
        Assert.False(FilterValues.Compare(IntKind.Value, FilterValue.From(2L), FilterOperator.Equal));

        Assert.True(FilterValues.Compare(UIntKind.Value, FilterValue.From(1L), FilterOperator.Equal));
        Assert.False(FilterValues.Compare(UIntKind.Value, FilterValue.From(-1L), FilterOperator.Equal));

        Assert.True(FilterValues.Compare(LongKind.Value, FilterValue.From(1L), FilterOperator.Equal));
        Assert.False(FilterValues.Compare(LongKind.Value, FilterValue.From(2L), FilterOperator.Equal));
    }

    private static void AssertFilter<TSubject>(
        FilterExpression filter,
        params FilterCase<TSubject>[] cases)
    {
        CompiledKernel immediate = FilterCompiler.Compile(
            typeof(TSubject),
            filter,
            FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(
            typeof(TSubject),
            filter,
            FilterCompilerOptions.Tiered);

        foreach (FilterCase<TSubject> item in cases)
        {
            Assert.Equal(item.Expected, immediate.Matches(item.Subject!));
            Assert.Equal(item.Expected, tiered.Matches(item.Subject!));
        }
    }

    private static NumericSubject Subject() =>
        new(
            Amount: NeighborInteger,
            UnsignedId: (ulong)NeighborInteger,
            Amounts: [NeighborInteger],
            LongIds: [NeighborInteger]);

    private sealed record DecimalFastPathSubject(double Score, double[] Scores) : IFilterSubject;
    private sealed record NumericSubject(
        decimal Amount,
        ulong UnsignedId,
        decimal[] Amounts,
        long[] LongIds) : IFilterSubject;

    private sealed record NullableArraySubject(int?[] OptionalCounts) : IFilterSubject;
    private sealed record FloatingSubject(double Score) : IFilterSubject;
    private sealed record IntegralSubject(int Count) : IFilterSubject;
    private sealed record UIntIndexSubject(uint Id) : IFilterSubject;
    private sealed record ULongIndexSubject(ulong Id) : IFilterSubject;
    private sealed record BigEnumSubject(BigEnum Kind) : IFilterSubject;
    private sealed record FilterCase<TSubject>(TSubject Subject, bool Expected);

    private enum HugeKind : ulong
    {
        First = 1,
        Last = ulong.MaxValue,
    }

    private enum BigEnum : ulong
    {
        Huge = ulong.MaxValue,
    }

    private enum ByteKind : byte { Value = 1 }
    private enum SByteKind : sbyte { Negative = -1, Value = 1 }
    private enum ShortKind : short { Value = 1 }
    private enum UShortKind : ushort { Value = 1 }
    private enum IntKind : int { Value = 1 }
    private enum UIntKind : uint { Value = 1 }
    private enum LongKind : long { Value = 1 }
}

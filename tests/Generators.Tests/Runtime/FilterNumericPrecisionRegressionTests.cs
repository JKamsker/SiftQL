using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class FilterNumericPrecisionRegressionTests
{
    public static void RunAll()
    {
        DecimalLiteralsUseNumericFastPaths();
        ProjectedDecimalFieldFiltersThroughDynamicSchema();
        ProjectionIncludeRequiredDoubleAcceptsDecimal();
        UnsignedEnumNumericLiteralDoesNotWrapToOutOfRangeValue();
        UnknownOrderedExpectedKindDoesNotFallBackToNumber();
    }

    private static void DecimalLiteralsUseNumericFastPaths()
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

    private static void ProjectedDecimalFieldFiltersThroughDynamicSchema()
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

    private static void ProjectionIncludeRequiredDoubleAcceptsDecimal()
    {
        var include = new EventProjectionInclude(
            "window",
            "result",
            [new EventProjectionArgument("seconds", FilterValue.From(1.5m))]);

        Assert.Equal(1.5D, ProjectionIncludeArguments.RequiredDouble(include, "seconds"));
    }

    private static void UnsignedEnumNumericLiteralDoesNotWrapToOutOfRangeValue()
    {
        bool equal = FilterValues.Compare(
            HugeKind.Last,
            FilterValue.From(-1L),
            FilterOperator.Equal);

        Assert.False(equal);
        Assert.True(FilterValues.Compare(HugeKind.First, FilterValue.From(1L), FilterOperator.Equal));
    }

    private static void UnknownOrderedExpectedKindDoesNotFallBackToNumber()
    {
        var invalid = new FilterValue { Kind = (FilterValueKind)999, Number = 1000D };

        Assert.False(FilterValues.Compare(10, invalid, FilterOperator.LessThan));
    }

    private sealed record DecimalFastPathSubject(double Score, double[] Scores) : IFilterSubject;

    private enum HugeKind : ulong
    {
        First = 1,
        Last = ulong.MaxValue,
    }
}

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterOperatorCoverageTests
{
    [Theory]
    [InlineData(FilterOperator.NotEqual, 10, 10, false)]
    [InlineData(FilterOperator.NotEqual, 10, 11, true)]
    [InlineData(FilterOperator.GreaterThanOrEqual, 10, 9, false)]
    [InlineData(FilterOperator.GreaterThanOrEqual, 10, 10, true)]
    [InlineData(FilterOperator.GreaterThanOrEqual, 10, 11, true)]
    [InlineData(FilterOperator.LessThanOrEqual, 10, 9, true)]
    [InlineData(FilterOperator.LessThanOrEqual, 10, 10, true)]
    [InlineData(FilterOperator.LessThanOrEqual, 10, 11, false)]
    public void NumericOperatorsMatchExpectedBoundaries(
        FilterOperator op,
        int expected,
        int actual,
        bool matches)
    {
        AssertFilter(
            FilterExpression.Compare(
                nameof(OperatorSubject.Count),
                op,
                FilterValue.From(expected)),
            new OperatorSubject(Count: actual),
            matches);
    }

    [Fact]
    public void ExistsDistinguishesNullFromEmptyStringAndNullableValues()
    {
        AssertFilter(
            FilterExpression.Exists(nameof(OperatorSubject.Name)),
            new OperatorSubject(Name: null),
            false);
        AssertFilter(
            FilterExpression.Exists(nameof(OperatorSubject.Name)),
            new OperatorSubject(Name: string.Empty),
            true);
        AssertFilter(
            FilterExpression.Exists(nameof(OperatorSubject.OptionalCount)),
            new OperatorSubject(OptionalCount: null),
            false);
        AssertFilter(
            FilterExpression.Exists(nameof(OperatorSubject.OptionalCount)),
            new OperatorSubject(OptionalCount: 0),
            true);
    }

    [Fact]
    public void StringFiltersUseOrdinalNullAndUnicodeSemantics()
    {
        AssertFilter(NameEquals(string.Empty), new OperatorSubject(Name: string.Empty), true);
        AssertFilter(NameEquals(string.Empty), new OperatorSubject(Name: null), false);
        AssertFilter(NameNotEquals(string.Empty), new OperatorSubject(Name: null), true);
        AssertFilter(NameEquals("cafe\u0301"), new OperatorSubject(Name: "café"), false);
        AssertFilter(NameEquals("snow-\u2603"), new OperatorSubject(Name: "snow-\u2603"), true);
    }

    [Fact]
    public void GuidFiltersMatchExactScalarAndArrayValues()
    {
        Guid accepted = Guid.NewGuid();

        AssertFilter(
            FilterExpression.Compare(
                nameof(OperatorSubject.Token),
                FilterOperator.Equal,
                FilterValue.From(accepted)),
            new OperatorSubject(Token: accepted),
            true);
        AssertFilter(
            FilterExpression.In(
                nameof(OperatorSubject.Token),
                [FilterValue.From(Guid.NewGuid()), FilterValue.From(accepted)]),
            new OperatorSubject(Token: accepted),
            true);
        AssertFilter(
            FilterExpression.Contains(
                nameof(OperatorSubject.Tokens),
                FilterValue.From(accepted)),
            new OperatorSubject { Tokens = [Guid.NewGuid(), accepted] },
            true);
    }

    [Fact]
    public void BooleanCombinationsRespectLogicalOperators()
    {
        FilterExpression filter = FilterExpression.And(
            FilterExpression.Compare(
                nameof(OperatorSubject.Active),
                FilterOperator.Equal,
                FilterValue.From(true)),
            FilterExpression.Or(
                FilterExpression.Compare(
                    nameof(OperatorSubject.Count),
                    FilterOperator.GreaterThanOrEqual,
                    FilterValue.From(10)),
                FilterExpression.Not(NameEquals("blocked"))));

        AssertFilter(filter, new OperatorSubject(Active: true, Count: 1, Name: "ok"), true);
        AssertFilter(filter, new OperatorSubject(Active: true, Count: 1, Name: "blocked"), false);
        AssertFilter(filter, new OperatorSubject(Active: true, Count: 10, Name: "blocked"), true);
        AssertFilter(filter, new OperatorSubject(Active: false, Count: 10, Name: "ok"), false);
    }

    private static FilterExpression NameEquals(string expected) =>
        FilterExpression.Compare(
            nameof(OperatorSubject.Name),
            FilterOperator.Equal,
            FilterValue.From(expected));

    private static FilterExpression NameNotEquals(string expected) =>
        FilterExpression.Compare(
            nameof(OperatorSubject.Name),
            FilterOperator.NotEqual,
            FilterValue.From(expected));

    private static void AssertFilter(
        FilterExpression filter,
        OperatorSubject subject,
        bool expected)
    {
        CompiledKernel immediate = FilterCompiler.Compile(
            typeof(OperatorSubject),
            filter,
            FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(
            typeof(OperatorSubject),
            filter,
            FilterCompilerOptions.Tiered);

        Assert.Equal(expected, immediate.Matches(subject));
        Assert.Equal(expected, tiered.Matches(subject));
    }

    private sealed record OperatorSubject(
        int Count = 0,
        int? OptionalCount = null,
        bool Active = false,
        string? Name = null,
        Guid Token = default) : IFilterSubject
    {
        public Guid[] Tokens { get; init; } = [];
    }
}

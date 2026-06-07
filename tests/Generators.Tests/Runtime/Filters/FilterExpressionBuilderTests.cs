using System.Linq.Expressions;
using SiftQL.Compiler;
using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterExpressionBuilderTests
{
    [Fact]
    public void ArrayBuilderBuildsGuidContainsFastPath()
    {
        Guid accepted = Guid.NewGuid();
        ParameterExpression parameter = Expression.Parameter(typeof(BuilderSubject), "subject");
        Expression access = Expression.Property(parameter, nameof(BuilderSubject.GuidIds));

        Expression? body = FilterExpressionArrayBuilder.BuildContains(
            access,
            FilterValue.From(accepted));

        Assert.NotNull(body);
        Func<BuilderSubject, bool> predicate = Expression
            .Lambda<Func<BuilderSubject, bool>>(body!, parameter)
            .Compile();

        Assert.True(predicate(new BuilderSubject { GuidIds = [Guid.NewGuid(), accepted] }));
        Assert.False(predicate(new BuilderSubject { GuidIds = [Guid.NewGuid()] }));
    }

    [Fact]
    public void InBuilderUsesOrdinalStringSemanticsForLookupAndEdgeCases()
    {
        ParameterExpression parameter = Expression.Parameter(typeof(BuilderSubject), "subject");
        Expression access = Expression.Property(parameter, nameof(BuilderSubject.Name));
        FilterValue[] values =
        [
            FilterValue.From(string.Empty),
            FilterValue.From("alpha"),
            FilterValue.From("cafe"),
            FilterValue.From("cafe\u0301"),
            FilterValue.From("snow-\u2603"),
        ];

        Expression? body = FilterExpressionInBuilder.Build(access, values);

        Assert.NotNull(body);
        Func<BuilderSubject, bool> predicate = Expression
            .Lambda<Func<BuilderSubject, bool>>(body!, parameter)
            .Compile();

        Assert.True(predicate(new BuilderSubject { Name = string.Empty }));
        Assert.True(predicate(new BuilderSubject { Name = "snow-\u2603" }));
        Assert.False(predicate(new BuilderSubject { Name = "café" }));
        Assert.False(predicate(new BuilderSubject { Name = null }));
    }

    [Theory]
    [InlineData(FilterOperator.NotEqual, 10, 10, false)]
    [InlineData(FilterOperator.NotEqual, 10, 11, true)]
    [InlineData(FilterOperator.GreaterThanOrEqual, 10, 9, false)]
    [InlineData(FilterOperator.GreaterThanOrEqual, 10, 10, true)]
    [InlineData(FilterOperator.LessThanOrEqual, 10, 11, false)]
    [InlineData(FilterOperator.LessThanOrEqual, 10, 10, true)]
    public void ScalarBuilderBuildsInclusiveAndNotEqualComparisons(
        FilterOperator op,
        int expected,
        int actual,
        bool matches)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(BuilderSubject), "subject");
        Expression access = Expression.Property(parameter, nameof(BuilderSubject.Count));

        Expression? body = FilterExpressionScalarBuilder.BuildCompare(
            access,
            FilterValue.From(expected),
            op);

        Assert.NotNull(body);
        Func<BuilderSubject, bool> predicate = Expression
            .Lambda<Func<BuilderSubject, bool>>(body!, parameter)
            .Compile();
        Assert.Equal(matches, predicate(new BuilderSubject { Count = actual }));
    }

    private sealed record BuilderSubject
    {
        public int Count { get; init; }
        public string? Name { get; init; }
        public Guid[] GuidIds { get; init; } = [];
    }
}

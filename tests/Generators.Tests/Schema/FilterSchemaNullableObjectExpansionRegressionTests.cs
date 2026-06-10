using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaNullableObjectExpansionRegressionTests
{
    [Fact]
    public void FallbackSchema_NullableReferenceValueObject_ExpandsNestedFields()
    {
        FilterSchema.RegisterValueObject<ExpansionLocation>();

        FilterSchema schema = FilterSchema.For(typeof(NullableLocationEvent));

        Assert.True(schema.TryGetField("Location", out _));
        Assert.True(schema.TryGetField("Location.Code", out _));
    }

    [Fact]
    public void Filters_NullableReferenceValueObject_NullPropagate()
    {
        FilterSchema.RegisterValueObject<ExpansionLocation>();
        FilterExpression filter = FilterExpression.Compare(
            "Location.Code",
            FilterOperator.Equal,
            FilterValue.From("AT"));

        var kernel = FilterCompiler.Compile(typeof(NullableLocationEvent), filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NullableLocationEvent(new ExpansionLocation("AT"))));
        Assert.False(kernel.Matches(new NullableLocationEvent(new ExpansionLocation("DE"))));
        Assert.False(kernel.Matches(new NullableLocationEvent(null)));
    }

    [Fact]
    public void LinqFilter_NullableReferenceValueObject_Compiles()
    {
        FilterSchema.RegisterValueObject<ExpansionLocation>();

        var query = QueryKernel.For<NullableLocationEvent>().Where(x => x.Location!.Code == "AT");
        var kernel = FilterCompiler.Compile(typeof(NullableLocationEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NullableLocationEvent(new ExpansionLocation("AT"))));
        Assert.False(kernel.Matches(new NullableLocationEvent(null)));
    }

    [Fact]
    public void FallbackSchema_NullableValueTypeObject_RemainsUnexpanded()
    {
        FilterSchema.RegisterValueObject<ExpansionCoordinate>();

        FilterSchema schema = FilterSchema.For(typeof(NullableCoordinateEvent));

        Assert.True(schema.TryGetField("Coordinate", out _));
        Assert.False(schema.TryGetField("Coordinate.X", out _));
    }

    [Fact]
    public void MemberNotEqualNull_LowersToExists()
    {
        FilterSchema.RegisterValueObject<ExpansionLocation>();
        FilterExpression filter = FilterExpression.Compare(
            "Location",
            FilterOperator.NotEqual,
            FilterValue.Null);

        var kernel = FilterCompiler.Compile(typeof(NullableLocationEvent), filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NullableLocationEvent(new ExpansionLocation("AT"))));
        Assert.False(kernel.Matches(new NullableLocationEvent(null)));
    }

    [Fact]
    public void MemberEqualNull_LowersToNotExists()
    {
        FilterSchema.RegisterValueObject<ExpansionLocation>();
        FilterExpression filter = FilterExpression.Compare(
            "Location",
            FilterOperator.Equal,
            FilterValue.Null);

        var kernel = FilterCompiler.Compile(typeof(NullableLocationEvent), filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NullableLocationEvent(null)));
        Assert.False(kernel.Matches(new NullableLocationEvent(new ExpansionLocation("AT"))));
    }

    [Fact]
    public void MemberPresenceCheck_InterpretedPath_LowersToExists()
    {
        FilterSchema.RegisterValueObject<ExpansionLocation>();
        // Between has no expression-tree builder, so the whole filter compiles via
        // the interpreted compiler -- exercising its presence-check path.
        FilterExpression filter = FilterExpression.And(
            FilterExpression.Compare("Location", FilterOperator.NotEqual, FilterValue.Null),
            FilterExpression.Between("Score", FilterValue.From(1L), FilterValue.From(10L)));

        var kernel = FilterCompiler.Compile(typeof(PresenceEvent), filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new PresenceEvent(new ExpansionLocation("AT"), 5)));
        Assert.False(kernel.Matches(new PresenceEvent(null, 5)));
        Assert.False(kernel.Matches(new PresenceEvent(new ExpansionLocation("AT"), 50)));
    }

    [Fact]
    public void MemberPresenceCheck_ParameterizedPlan_LowersToExists()
    {
        FilterSchema.RegisterValueObject<ExpansionLocation>();
        // The parameter forces the parameterized plan builder, whose BuildCompare
        // must also lower the object null-check to Exists.
        FilterExpression filter = FilterExpression.And(
            FilterExpression.Compare("Location", FilterOperator.NotEqual, FilterValue.Null),
            FilterExpression.Compare(
                "Location.Code",
                FilterOperator.Equal,
                FilterValue.From("AT") with { ParameterKey = "code" }));

        var kernel = FilterCompiler.Compile(typeof(NullableLocationEvent), filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NullableLocationEvent(new ExpansionLocation("AT"))));
        Assert.False(kernel.Matches(new NullableLocationEvent(null)));
        Assert.False(kernel.Matches(new NullableLocationEvent(new ExpansionLocation("DE"))));
    }

    [Fact]
    public void Validator_AcceptsMemberPresenceCheck()
    {
        FilterSchema.RegisterValueObject<ExpansionLocation>();
        FilterExpression filter = FilterExpression.Compare(
            "Location",
            FilterOperator.NotEqual,
            FilterValue.Null);

        FilterValidationResult result = FilterValidator.Validate(typeof(NullableLocationEvent), filter);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void LinqMemberNotNull_LowersToExists()
    {
        FilterSchema.RegisterValueObject<ExpansionLocation>();

        var query = QueryKernel.For<NullableLocationEvent>().Where(x => x.Location != null);
        var kernel = FilterCompiler.Compile(typeof(NullableLocationEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NullableLocationEvent(new ExpansionLocation("AT"))));
        Assert.False(kernel.Matches(new NullableLocationEvent(null)));
    }

    [Fact]
    public void LinqMemberEqualNull_LowersToNotExists()
    {
        FilterSchema.RegisterValueObject<ExpansionLocation>();

        var query = QueryKernel.For<NullableLocationEvent>().Where(x => x.Location == null);
        var kernel = FilterCompiler.Compile(typeof(NullableLocationEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NullableLocationEvent(null)));
        Assert.False(kernel.Matches(new NullableLocationEvent(new ExpansionLocation("AT"))));
    }

    public sealed record ExpansionLocation(string Code);

    public sealed record PresenceEvent(ExpansionLocation? Location, int Score) : IFilterSubject;

    public sealed record NullableLocationEvent(ExpansionLocation? Location) : IFilterSubject;

    public readonly record struct ExpansionCoordinate(int X);

    public sealed record NullableCoordinateEvent(ExpansionCoordinate? Coordinate) : IFilterSubject;
}

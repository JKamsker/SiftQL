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

    public sealed record ExpansionLocation(string Code);

    public sealed record NullableLocationEvent(ExpansionLocation? Location) : IFilterSubject;

    public readonly record struct ExpansionCoordinate(int X);

    public sealed record NullableCoordinateEvent(ExpansionCoordinate? Coordinate) : IFilterSubject;
}

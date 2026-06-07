using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterIndexExtractorTests
{
    private sealed record SubjectA(
        int Id = 0,
        string Region = "",
        bool Flag = false,
        long LargeId = 0,
        double Score = 0.0,
        float FloatScore = 0f,
        decimal Price = 0m,
        ulong ULargeId = 0,
        byte ByteVal = 0,
        Guid Token = default,
        SubjectStatus Status = SubjectStatus.None) : IFilterSubject;

    public enum SubjectStatus { None = 0, Active = 1, Suspended = 2 }

    [Fact]
    public void Extractor_NullExpression_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA), null));

    [Fact]
    public void Extractor_AnyExpression_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA), FilterExpression.Any));

    [Fact]
    public void Extractor_SimpleEqual_ReturnsKey()
    {
        var expr = FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(42L));
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA), expr);
        Assert.NotNull(key);
        Assert.Equal(nameof(SubjectA.Id), key!.Field, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(42L, key.Value.Integer);
    }

    [Fact]
    public void Extractor_NotEqualOperator_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.NotEqual, FilterValue.From(1L))));

    [Fact]
    public void Extractor_GreaterThanOperator_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.GreaterThan, FilterValue.From(1L))));

    [Fact]
    public void Extractor_And_SelectsMostSelectiveKey()
    {
        var expr = FilterExpression.And(
            FilterExpression.Compare(nameof(SubjectA.Region), FilterOperator.Equal, FilterValue.From("north")),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(5L)));
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA), expr);
        Assert.NotNull(key);
        Assert.Equal(nameof(SubjectA.Id), key!.Field, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extractor_And_AllNonIndexable_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA), FilterExpression.And(
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.GreaterThan, FilterValue.From(0L)),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.LessThan, FilterValue.From(100L)))));

    [Fact]
    public void Extractor_DecimalField_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Price), FilterOperator.Equal, FilterValue.From(1.5m))));

    [Fact]
    public void Extractor_EnumStringValue_ReturnsKey()
    {
        var expr = FilterExpression.Compare(nameof(SubjectA.Status), FilterOperator.Equal, FilterValue.From(nameof(SubjectStatus.Active)));
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA), expr);
        Assert.NotNull(key);
        Assert.Equal(1L, key!.Value.Integer);
    }

    [Fact]
    public void Extractor_EnumIntValue_ReturnsKey()
    {
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Status), FilterOperator.Equal, FilterValue.From(2L)));
        Assert.NotNull(key);
        Assert.Equal(2L, key!.Value.Integer);
    }

    [Fact]
    public void Extractor_DoubleField_NumberKind_ReturnsKey()
        => Assert.NotNull(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Score), FilterOperator.Equal, FilterValue.From(3.14))));

    [Fact]
    public void Extractor_ULargeId_NegativeInteger_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.ULargeId), FilterOperator.Equal, FilterValue.From(-1L))));

    [Fact]
    public void Extractor_LongField_IntegerKind_ReturnsKey()
    {
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.LargeId), FilterOperator.Equal, FilterValue.From(12345L)));
        Assert.NotNull(key);
        Assert.Equal(12345L, key!.Value.Integer);
    }

    [Fact]
    public void Extractor_StringField_ReturnsStringKey()
    {
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Region), FilterOperator.Equal, FilterValue.From("west")));
        Assert.NotNull(key);
        Assert.Equal("west", key!.Value.String);
    }

    [Fact]
    public void Extractor_GuidField_ReturnsKey()
        => Assert.NotNull(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Token), FilterOperator.Equal, FilterValue.From(Guid.NewGuid()))));

    [Fact]
    public void Extractor_OrExpression_ReturnsNull()
        => Assert.Null(FilterIndexExtractor.Extract(typeof(SubjectA), FilterExpression.Or(
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(1L)),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(2L)))));

    [Fact]
    public void Extractor_UnsignedSmallValue_ReturnsIntegerKey()
    {
        FilterIndexKey? key = FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Id), FilterOperator.Equal, FilterValue.From(100UL)));
        Assert.NotNull(key);
        Assert.Equal(100L, key!.Value.Integer);
    }

    [Fact]
    public void Extractor_BooleanField_ReturnsKey()
        => Assert.NotNull(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.Flag), FilterOperator.Equal, FilterValue.From(true))));

    [Fact]
    public void Extractor_FloatField_IntegerKind_ReturnsKey()
        => Assert.NotNull(FilterIndexExtractor.Extract(typeof(SubjectA),
            FilterExpression.Compare(nameof(SubjectA.FloatScore), FilterOperator.Equal, FilterValue.From(5L))));
}

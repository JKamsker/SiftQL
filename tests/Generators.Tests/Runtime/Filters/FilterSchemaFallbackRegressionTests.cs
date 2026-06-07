using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaFallbackRegressionTests
{
    [Fact]
    public void RegisterValueObjectInvalidatesPreviouslyBuiltFallbackSchemas()
    {
        FilterSchema beforeRegistration = FilterSchema.For(typeof(LateRegisteredLocationEvent));
        Assert.False(beforeRegistration.TryGetField("Location.MapId", out _));

        FilterSchema.RegisterValueObject<LateRegisteredLocation>();
        FilterSchema afterRegistration = FilterSchema.For(typeof(LateRegisteredLocationEvent));

        Assert.True(afterRegistration.TryGetField("Location.MapId", out _));
    }

    [Fact]
    public void NullableApprovedValueObjectDoesNotCrashFallbackSchema()
    {
        FilterSchema.RegisterValueObject<MapLocation>();

        FilterSchema schema = FilterSchema.For(typeof(NullableLocationEvent));

        Assert.True(schema.TryGetField(nameof(NullableLocationEvent.Location), out _));
        Assert.False(schema.TryGetField("Location.MapId", out _));
    }

    [Fact]
    public void NullableReferenceValueObjectDoesNotExposeUnsafeNestedFallbackFields()
    {
        FilterSchema.RegisterValueObject<ReferenceLocation>();

        FilterSchema schema = FilterSchema.For(typeof(NullableReferenceLocationEvent));

        Assert.True(schema.TryGetField(nameof(NullableReferenceLocationEvent.Location), out _));
        Assert.False(schema.TryGetField("Location.MapId", out _));
    }

    private readonly record struct MapLocation(long MapId, int X, int Y);

    private sealed record NullableLocationEvent(
        Guid EventId,
        MapLocation? Location) : IFilterSubject;

    private sealed record LateRegisteredLocation(long MapId);

    private sealed record LateRegisteredLocationEvent(
        Guid EventId,
        LateRegisteredLocation Location) : IFilterSubject;

    private sealed record ReferenceLocation(long MapId);

    private sealed record NullableReferenceLocationEvent(
        Guid EventId,
        ReferenceLocation? Location) : IFilterSubject;
}

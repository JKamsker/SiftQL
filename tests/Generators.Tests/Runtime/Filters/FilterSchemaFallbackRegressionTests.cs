using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class FilterSchemaFallbackRegressionTests
{
    public static void RunAll()
    {
        NullableApprovedValueObjectDoesNotCrashFallbackSchema();
    }

    private static void NullableApprovedValueObjectDoesNotCrashFallbackSchema()
    {
        FilterSchema.RegisterValueObject<MapLocation>();

        FilterSchema schema = FilterSchema.For(typeof(NullableLocationEvent));

        Assert.True(schema.TryGetField(nameof(NullableLocationEvent.Location), out _));
        Assert.False(schema.TryGetField("Location.MapId", out _));
    }

    private readonly record struct MapLocation(long MapId, int X, int Y);

    private sealed record NullableLocationEvent(
        Guid EventId,
        MapLocation? Location) : IFilterSubject;
}

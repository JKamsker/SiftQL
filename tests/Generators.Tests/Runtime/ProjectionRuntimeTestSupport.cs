using MessagePack;
using MessagePack.Resolvers;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class ProjectionRuntimeTestSupport
{
    public static MessagePackSerializerOptions PayloadOptions { get; } =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    public static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    public static CompiledProjection<object>.IncludeProjector CompileInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            static (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar("included")));
    }

    public static ProjectedEvent Deserialize(ReadOnlyMemory<byte> payload) =>
        MessagePackSerializer.Deserialize<ProjectedEvent>(payload, PayloadOptions);

    public static void AssertEquivalent(ProjectedEvent expected, ProjectedEvent actual)
    {
        Assert.Equal(expected.EventType, actual.EventType);
        Assert.Equal(expected.EventName, actual.EventName);
        AssertFields(expected.Fields, actual.Fields);
        AssertFields(expected.Context, actual.Context);
    }

    public static async Task<TieredProjectionSnapshot> WaitForSnapshotAsync(
        CompiledProjection<object> projection,
        Func<TieredProjectionSnapshot, bool> predicate)
    {
        for (int i = 0; i < 500; i++)
        {
            TieredProjectionSnapshot? snapshot = projection.TieredSnapshot;
            if (snapshot is not null && predicate(snapshot))
                return snapshot;

            await Task.Delay(10);
        }

        throw new InvalidOperationException(
            $"Tiered projection did not reach expected state. Last snapshot: {projection.TieredSnapshot}");
    }

    private static void AssertFields(ProjectedEventField[] expected, ProjectedEventField[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Name, actual[i].Name);
            Assert.Equal(expected[i].Value, actual[i].Value);
        }
    }
}

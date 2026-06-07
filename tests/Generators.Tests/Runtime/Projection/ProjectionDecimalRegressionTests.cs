using MessagePack;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class ProjectionDecimalRegressionTests
{
    private const long RoundedInteger = 9_007_199_254_740_992L;
    private const long NeighborInteger = 9_007_199_254_740_993L;

    public static void RunAll()
    {
        NonIntegralDecimalProjectionDoesNotUseDoubleCarrier()
            .GetAwaiter()
            .GetResult();
        NonIntegralDecimalFilterValueDoesNotUseDoubleCarrier();
        IntegralDecimalProjectionKeepsExactIntegerValue()
            .GetAwaiter()
            .GetResult();
        ProjectedDecimalFilterRejectsRoundedNeighbor()
            .GetAwaiter()
            .GetResult();
        UnsignedProjectionValuePreservesLargeUInt64();
        PayloadProjectionPreservesLargeUInt64()
            .GetAwaiter()
            .GetResult();
        ProjectedUnsignedFilterMatchesLargeUInt64()
            .GetAwaiter()
            .GetResult();
    }

    private static async Task NonIntegralDecimalProjectionDoesNotUseDoubleCarrier()
    {
        const decimal expected = 12_345_678_901_234_567_890.1234m;
        var subject = new DecimalSubject(expected);
        var projection = ProjectionCompiler.Compile<object>(
            typeof(DecimalSubject),
            EventProjectionExpression.Select(nameof(DecimalSubject.Amount)),
            ProjectionRuntimeTestSupport.RejectInclude);

        ProjectedEvent projected = await projection.ProjectAsync(
            subject,
            new object(),
            CancellationToken.None);
        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            subject,
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None);
        ProjectedEvent roundTripped = MessagePackSerializer.Deserialize<ProjectedEvent>(
            payload,
            ProjectionRuntimeTestSupport.PayloadOptions);

        AssertDecimalField(projected, expected);
        AssertDecimalField(roundTripped, expected);
    }

    private static void NonIntegralDecimalFilterValueDoesNotUseDoubleCarrier()
    {
        FilterValue value = FilterValue.FromObject(0.1234567890123456789012345678m);

        Assert.NotEqual(FilterValueKind.Number, value.Kind);
        Assert.Equal(FilterValueKind.Decimal, value.Kind);
    }

    private static async Task IntegralDecimalProjectionKeepsExactIntegerValue()
    {
        var projection = ProjectionCompiler.Compile<object>(
            typeof(DecimalSubject),
            EventProjectionExpression.Select(nameof(DecimalSubject.Amount)),
            ProjectionRuntimeTestSupport.RejectInclude);

        ProjectedEvent projected = await projection.ProjectAsync(
            new DecimalSubject(NeighborInteger),
            new object(),
            CancellationToken.None);

        ProjectedEventValue value = projected.Field(nameof(DecimalSubject.Amount));
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(NeighborInteger, value.Integer);
    }

    private static async Task ProjectedDecimalFilterRejectsRoundedNeighbor()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(nameof(DecimalEvent.Amount)))
            .AppendFilter(FilterExpression.Compare(
                ProjectedEventPaths.Field(nameof(DecimalEvent.Amount)),
                FilterOperator.Equal,
                FilterValue.From(RoundedInteger)));
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(DecimalEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new DecimalEvent(Guid.NewGuid(), NeighborInteger),
            new object(),
            CancellationToken.None);

        Assert.Null(projected);
    }

    private static void UnsignedProjectionValuePreservesLargeUInt64()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(ulong.MaxValue);

        AssertUnsigned(value, ulong.MaxValue);
    }

    private static async Task PayloadProjectionPreservesLargeUInt64()
    {
        var projection = ProjectionCompiler.Compile<object>(
            typeof(UnsignedSubject),
            EventProjectionExpression.Select(nameof(UnsignedSubject.Id)),
            ProjectionRuntimeTestSupport.RejectInclude);

        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            new UnsignedSubject(ulong.MaxValue),
            new object(),
            ProjectionRuntimeTestSupport.PayloadOptions,
            CancellationToken.None);
        ProjectedEvent projected = MessagePackSerializer.Deserialize<ProjectedEvent>(
            payload,
            ProjectionRuntimeTestSupport.PayloadOptions);

        AssertUnsigned(projected.Field(nameof(UnsignedSubject.Id)), ulong.MaxValue);
    }

    private static async Task ProjectedUnsignedFilterMatchesLargeUInt64()
    {
        EventPipelineExpression pipeline = QueryKernel.For<UnsignedEvent>()
            .Select(static ev => ev.Id)
            .WhereProjected(static projected =>
                projected.Field(nameof(UnsignedEvent.Id)).UnsignedInteger == ulong.MaxValue)
            .Select(nameof(UnsignedEvent.Id))
            .Pipeline;
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(UnsignedEvent),
            pipeline,
            ProjectionRuntimeTestSupport.RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new UnsignedEvent(Guid.NewGuid(), ulong.MaxValue),
            new object(),
            CancellationToken.None);

        Assert.NotNull(projected);
        AssertUnsigned(projected!.Field(nameof(UnsignedEvent.Id)), ulong.MaxValue);
    }

    private static void AssertDecimalField(ProjectedEvent projected, decimal expected)
    {
        Assert.True(projected.TryGetField(nameof(DecimalSubject.Amount), out ProjectedEventValue actual));
        Assert.Equal(ProjectedEventValueKind.Decimal, actual.Kind);
        Assert.Equal(expected, actual.Decimal);
    }

    private static void AssertUnsigned(ProjectedEventValue value, ulong expected)
    {
        Assert.Equal(ProjectedEventValueKind.UnsignedInteger, value.Kind);
        Assert.Equal(expected, value.UnsignedInteger);
    }

    private sealed record DecimalSubject(decimal Amount);
    private sealed record UnsignedSubject(ulong Id) : IFilterSubject;
    private sealed record DecimalEvent(Guid EventId, decimal Amount) : IFilterSubject;
    private sealed record UnsignedEvent(Guid EventId, ulong Id) : IFilterSubject;
}

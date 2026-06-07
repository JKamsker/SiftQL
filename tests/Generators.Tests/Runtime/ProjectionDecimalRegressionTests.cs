using MessagePack;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

internal static class ProjectionDecimalRegressionTests
{
    public static void RunAll()
    {
        NonIntegralDecimalProjectionDoesNotUseDoubleCarrier()
            .GetAwaiter()
            .GetResult();
        NonIntegralDecimalFilterValueDoesNotUseDoubleCarrier();
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

    private static void AssertDecimalField(ProjectedEvent projected, decimal expected)
    {
        Assert.True(projected.TryGetField(nameof(DecimalSubject.Amount), out ProjectedEventValue actual));
        Assert.Equal(ProjectedEventValueKind.Decimal, actual.Kind);
        Assert.Equal(expected, actual.Decimal);
    }

    private sealed record DecimalSubject(decimal Amount);
}

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionParameterValueRegressionTests
{
    [Fact]
    public void ProjectionIncludesRejectDuplicateParameterKeysWithSignedZeroValues()
    {
        var projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude(
                "test.zero",
                "zero",
                [
                    new EventProjectionArgument(
                        "first",
                        FilterValue.From(0.0D) with { ParameterKey = "p0" }),
                    new EventProjectionArgument(
                        "second",
                        FilterValue.From(-0.0D) with { ParameterKey = "p0" }),
                ]),
        ]);

        FilterValidationException exception = Assert.Throws<FilterValidationException>(() =>
            ProjectionCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                projection,
                CompileNoopInclude));

        Assert.Contains("p0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionFingerprintsPreserveTimestampOffsets()
    {
        var utc = new DateTimeOffset(2026, 2, 3, 12, 0, 0, TimeSpan.Zero);
        var offset = new DateTimeOffset(2026, 2, 3, 14, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(utc.UtcTicks, offset.UtcTicks);
        Assert.NotEqual(
            ProjectionExpressionFingerprint.Create(TimestampProjection(utc)),
            ProjectionExpressionFingerprint.Create(TimestampProjection(offset)));
    }

    [Fact]
    public void ProjectionIncludesRejectDuplicateParameterKeysWithOffsetEquivalentTimestamps()
    {
        var utc = new DateTimeOffset(2026, 2, 3, 12, 0, 0, TimeSpan.Zero);
        var offset = new DateTimeOffset(2026, 2, 3, 14, 0, 0, TimeSpan.FromHours(2));
        var projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude(
                "test.timestamp",
                "timestamp",
                [
                    new EventProjectionArgument(
                        "first",
                        FilterValue.From(utc) with { ParameterKey = "p0" }),
                    new EventProjectionArgument(
                        "second",
                        FilterValue.From(offset) with { ParameterKey = "p0" }),
                ]),
        ]);

        FilterValidationException exception = Assert.Throws<FilterValidationException>(() =>
            ProjectionCompiler.Compile<object>(
                typeof(ItemUsedEvent),
                projection,
                CompileNoopInclude));

        Assert.Contains("p0", exception.Message, StringComparison.Ordinal);
    }

    private static EventProjectionExpression TimestampProjection(DateTimeOffset timestamp) =>
        EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude(
                "test.timestamp",
                "timestamp",
                [new EventProjectionArgument("instant", FilterValue.From(timestamp))]),
        ]);

    private static CompiledProjection<object>.IncludeProjector CompileNoopInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            static (_, _, _) => ValueTask.FromResult(ProjectedEventValue.Null));
    }
}

using System.Globalization;
using System.Reflection;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;

namespace SiftQL.Generators.Tests;

public sealed class QueryKernelContextIncludeKeyRegressionTests
{
    [Fact]
    public async Task ContextIncludeKeysDoNotCollideWithStringArgumentDelimiters()
    {
        const string left = "left-value";
        const string right = "right-value";
        const string emptyGuid = "00000000-0000-0000-0000-000000000000";
        const string collision = left + ":" + emptyGuid +
            ":0|right:0:4:False:0:0:0:0:" + right;
        var query = QueryKernel.For<ContextKeyEvent, CollisionContext>()
            .Select((ev, ctx) => new
            {
                One = ctx.Echo(collision),
                Two = ctx.Echo(left, right),
            });
        CompiledEventPipeline<CollisionContext> compiled = EventPipelineCompiler.Compile<CollisionContext>(
            typeof(ContextKeyEvent),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? projected = await compiled.ProjectAsync(
            new ContextKeyEvent(1),
            new CollisionContext(),
            CancellationToken.None);

        Assert.NotNull(projected);
        Assert.Equal("one:" + collision, projected!.Field("One").String);
        Assert.Equal("two:" + left + ":" + right, projected.Field("Two").String);
    }

    [Fact]
    public void ContextIncludeKeysAreInvariantAcrossCultures()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var query = QueryKernel.For<ContextKeyEvent, CultureContext>();

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            query = query.Where((_, ctx) => ctx.Score(1.5D) > 0);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            query = query.Where((_, ctx) => ctx.Score(1.5D) > 0);

            EventProjectionInclude[] includes = query.Pipeline.Stages
                .Where(static stage => stage.Kind == EventPipelineStageKind.Projection)
                .SelectMany(static stage => stage.Projection.Includes)
                .ToArray();

            Assert.Single(includes);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ContextIncludeLiteralKeysUseInvariantPrimitiveFormatting()
    {
        FilterValue[] values =
        [
            FilterValue.From(true),
            FilterValue.From(-42L),
            FilterValue.From(ulong.MaxValue),
            FilterValue.From(1.5D),
            FilterValue.From(1.5m),
            FilterValue.From(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero)),
        ];
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            string[] germanKeys = values.Select(IncludeKey).ToArray();

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            string[] usKeys = values.Select(IncludeKey).ToArray();

            Assert.Equal(usKeys, germanKeys);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private sealed record ContextKeyEvent(long Id) : IFilterSubject;

    private static string IncludeKey(FilterValue value)
    {
        Type type = typeof(QueryKernel).Assembly.GetType(
            "SiftQL.Translation.ContextExpressionIncludes",
            throwOnError: true)!;
        MethodInfo method = type.GetMethod(
            "IncludeKey",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)method.Invoke(
            null,
            ["test.literal", new[] { new EventProjectionArgument("value", value) }])!;
    }

    private sealed class CollisionContext
    {
        public string Echo(string left) => "one:" + left;

        public string Echo(string left, string right) => "two:" + left + ":" + right;
    }

    private sealed class CultureContext
    {
        public long Score(double value) => value > 0 ? 1 : 0;
    }
}

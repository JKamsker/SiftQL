using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectedEventNullShapeRegressionTests
{
    [Fact]
    public async Task CompiledProjectionTreatsNullInheritedContextAsEmpty()
    {
        CompiledProjection<object> projection = ProjectionCompiler.Compile<object>(
            typeof(ProjectedEvent),
            EventProjectionExpression.Default.WithFields(
            [
                new EventProjectionField(ProjectedEventPaths.Field("ItemId"), "ItemId"),
            ]),
            RejectInclude,
            ProjectionCompilerOptions.Immediate);
        var source = new ProjectedEvent
        {
            EventType = "Projected",
            EventName = "Projected",
            Fields = [new ProjectedEventField("ItemId", ProjectedEventValue.FromScalar(100L))],
            Context = null!,
        };

        ProjectedEvent projected = await projection.ProjectAsync(source, new object(), CancellationToken.None);

        Assert.Equal(100, projected.Field("ItemId").Integer);
        Assert.Empty(projected.Context);
    }

    [Fact]
    public async Task ProjectedFilterTreatsObjectValueWithNullFieldsAsMissing()
    {
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            typeof(ProjectedEvent),
            EventPipelineExpression.Default.AppendFilter(
                FilterExpression.Exists(ProjectedEventPaths.Field("Player.Id"))),
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);
        var source = new ProjectedEvent
        {
            EventType = "Projected",
            EventName = "Projected",
            Fields =
            [
                new ProjectedEventField(
                    "Player",
                    new ProjectedEventValue
                    {
                        Kind = ProjectedEventValueKind.Object,
                        Fields = null!,
                    }),
            ],
        };

        ProjectedEvent? projected = await compiled.ProjectAsync(source, new object(), CancellationToken.None);

        Assert.Null(projected);
    }

    [Fact]
    public async Task CompiledProjectionSkipsNullInheritedContextEntries()
    {
        CompiledProjection<object> projection = ProjectionCompiler.Compile<object>(
            typeof(ProjectedEvent),
            EventProjectionExpression.Default.WithFields(
            [
                new EventProjectionField(ProjectedEventPaths.Context("tag"), "Tag"),
            ]),
            RejectInclude,
            ProjectionCompilerOptions.Immediate);
        var source = new ProjectedEvent
        {
            EventType = "Projected",
            EventName = "Projected",
            Context =
            [
                null!,
                new ProjectedEventField("tag", ProjectedEventValue.FromScalar("selected")),
                new ProjectedEventField("keep", ProjectedEventValue.FromScalar("inherited")),
            ],
        };

        ProjectedEvent projected = await projection.ProjectAsync(
            source,
            new object(),
            CancellationToken.None);

        Assert.Equal("selected", projected.Field("Tag").String);
        ProjectedEventField inherited = Assert.Single(projected.Context);
        Assert.Equal("keep", inherited.Name);
        Assert.Equal("inherited", inherited.Value.String);
    }

    [Fact]
    public async Task CompiledProjectionConsumesFlatDottedContextField()
    {
        CompiledProjection<object> projection = ProjectionCompiler.Compile<object>(
            typeof(ProjectedEvent),
            EventProjectionExpression.Default.WithFields(
            [
                new EventProjectionField(ProjectedEventPaths.Context("lookup.tag"), "Tag"),
            ]),
            RejectInclude,
            ProjectionCompilerOptions.Immediate);
        var source = new ProjectedEvent
        {
            EventType = "Projected",
            EventName = "Projected",
            Context =
            [
                new ProjectedEventField("lookup.tag", ProjectedEventValue.FromScalar("selected")),
                new ProjectedEventField("keep", ProjectedEventValue.FromScalar("inherited")),
            ],
        };

        ProjectedEvent projected = await projection.ProjectAsync(
            source,
            new object(),
            CancellationToken.None);

        Assert.Equal("selected", projected.Field("Tag").String);
        ProjectedEventField inherited = Assert.Single(projected.Context);
        Assert.Equal("keep", inherited.Name);
        Assert.Equal("inherited", inherited.Value.String);
    }

    [Fact]
    public async Task IncludedContextRootReplacesInheritedDottedChildren()
    {
        CompiledProjection<object> projection = ProjectionCompiler.Compile<object>(
            typeof(ProjectedEvent),
            EventProjectionExpression.Default.WithIncludes(
            [
                new EventProjectionInclude("test.lookup", "lookup"),
            ]),
            CompileLookupInclude,
            ProjectionCompilerOptions.Immediate);
        var source = new ProjectedEvent
        {
            EventType = "Projected",
            EventName = "Projected",
            Context =
            [
                new ProjectedEventField("lookup.tag", ProjectedEventValue.FromScalar("old")),
                new ProjectedEventField("keep", ProjectedEventValue.FromScalar("inherited")),
            ],
        };

        ProjectedEvent projected = await projection.ProjectAsync(
            source,
            new object(),
            CancellationToken.None);

        ProjectedEventValue lookup = projected.ContextValue("lookup");
        Assert.Equal(ProjectedEventValueKind.Object, lookup.Kind);
        ProjectedEventField tag = Assert.Single(lookup.Fields);
        Assert.Equal("tag", tag.Name);
        Assert.Equal("new", tag.Value.String);
        Assert.DoesNotContain(projected.Context, static field => field.Name == "lookup.tag");
        Assert.Equal("inherited", projected.ContextValue("keep").String);
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private static CompiledProjection<object>.IncludeProjector CompileLookupInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            static (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromFields(
            [
                new ProjectedEventField("tag", ProjectedEventValue.FromScalar("new")),
            ])));
    }
}

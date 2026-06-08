using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratedContextProjectionHotModeTests
{
    private const string EventTypeName = "Plugin.Events.ContextProjectionEvent";

    [Fact]
    public async Task GeneratedHotHandlesContextSourceFieldIncludeAndProjectedContextFilter()
    {
        string assemblyName = "Plugin.Matrix.ContextProjection.GeneratedHot";
        EventProjectionExpression projection = ContextProjection();
        FilterExpression filter = ContextFilter();
        using var context = GeneratedModeMatrixSupport.LoadContext(
            GeneratedExecutionMode.GeneratedHot,
            assemblyName,
            EventTree(),
            EventTypeName,
            "generated context projection hot provider",
            GeneratedModeMatrixSupport.ProjectionEntry(Subject(assemblyName), projection),
            GeneratedModeMatrixSupport.FilterEntry(ProjectedSubject(assemblyName), filter));

        Guid thiefId = Guid.NewGuid();
        Guid warriorId = Guid.NewGuid();
        var combat = new CombatContext(
            new Player(thiefId, Profession.Thief),
            new Player(warriorId, Profession.Warrior));
        CompiledProjection<CombatContext> compiledProjection = ProjectionCompiler.Compile(
            context.EventType,
            projection,
            ProjectionContextIncludeCompiler.Compile<CombatContext>,
            GeneratedModeMatrixSupport.ProjectionOptions(GeneratedExecutionMode.GeneratedHot));
        ProjectedEvent projected = await compiledProjection.ProjectAsync(
            Event(context.EventType, thiefId, Guid.NewGuid(), pluginId: 7, contentId: 11, duration: 2.5),
            combat,
            CancellationToken.None);
        CompiledKernel compiledFilter = FilterCompiler.CompileWithSchema(
            typeof(ProjectedEvent),
            filter,
            GeneratedModeMatrixSupport.FilterOptions(GeneratedExecutionMode.GeneratedHot),
            errorFactory: null,
            _ => ProjectedEventFilterSchema.ForFilter(filter));

        Assert.False(compiledProjection.IsTiered);
        Assert.False(compiledFilter.IsTiered);
        Assert.Equal(thiefId, projected.Field("tg").Guid);
        Assert.Equal(nameof(Profession.Thief), projected.ContextValue("prof").String);
        Assert.True(compiledFilter.Matches(projected));

        ProjectedEvent rejected = await compiledProjection.ProjectAsync(
            Event(context.EventType, warriorId, Guid.NewGuid(), pluginId: 7, contentId: 11, duration: 2.5),
            combat,
            CancellationToken.None);
        Assert.False(compiledFilter.Matches(rejected));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public async Task ConstantIncludeProjectionPipelineReturnsSameFinalField(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.ContextProjection.Constant." + mode;
        string runtimeLabel = "runtime-label";
        EventProjectionExpression manifestSourceProjection = ConstantSourceProjection("manifest-label");
        EventProjectionExpression runtimeSourceProjection = ConstantSourceProjection(runtimeLabel);
        EventProjectionExpression finalProjection = ConstantFinalProjection();
        using var context = GeneratedModeMatrixSupport.LoadContext(
            mode,
            assemblyName,
            EventTree(),
            EventTypeName,
            "constant projection pipeline provider",
            GeneratedModeMatrixSupport.ProjectionEntry(Subject(assemblyName), manifestSourceProjection),
            GeneratedModeMatrixSupport.ProjectionEntry(ProjectedSubject(assemblyName), finalProjection));
        CompiledProjection<object> sourceProjection = ProjectionCompiler.Compile(
            context.EventType,
            runtimeSourceProjection,
            ProjectionContextIncludeCompiler.Compile<object>,
            GeneratedModeMatrixSupport.ProjectionOptions(mode));
        CompiledProjection<object> projectedProjection = ProjectionCompiler.Compile(
            typeof(ProjectedEvent),
            finalProjection,
            ProjectionContextIncludeCompiler.Compile<object>,
            GeneratedModeMatrixSupport.ProjectionOptions(mode));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(runtimeSourceProjection)
            .AppendProjection(finalProjection);
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile(
            context.EventType,
            pipeline,
            ProjectionContextIncludeCompiler.Compile<object>,
            GeneratedModeMatrixSupport.PipelineOptions(mode));

        ProjectedEvent? projected = await compiled.ProjectAsync(
            Event(context.EventType, Guid.NewGuid(), Guid.NewGuid(), pluginId: 7, contentId: 11, duration: 2.5),
            new object(),
            CancellationToken.None);

        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, sourceProjection.IsTiered);
        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, projectedProjection.IsTiered);
        Assert.NotNull(projected);
        Assert.Equal(runtimeLabel, projected!.Field("Label").String);
    }

    private static EventProjectionExpression ContextProjection() =>
        EventProjectionExpression.Default
            .WithFields(
            [
                new EventProjectionField("PluginId"),
                new EventProjectionField("ContentId"),
                new EventProjectionField("TargetId", "tg"),
                new EventProjectionField("SourceId", "src"),
                new EventProjectionField("Duration", "dur"),
            ])
            .WithIncludes(
            [
                new EventProjectionInclude(
                    "siftql.context.method:GetPlayer.Profession",
                    "prof",
                    [EventProjectionArgument.FromSourceField("id", "TargetId")]),
            ]);

    private static FilterExpression ContextFilter() =>
        FilterExpression.And(
            FilterExpression.Compare(
                ProjectedEventPaths.Field("PluginId"),
                FilterOperator.Equal,
                FilterValue.From(7L)),
            FilterExpression.Compare(
                ProjectedEventPaths.Field("ContentId"),
                FilterOperator.Equal,
                FilterValue.From(11L)),
            FilterExpression.Compare(
                ProjectedEventPaths.Context("prof"),
                FilterOperator.Equal,
            FilterValue.From(nameof(Profession.Thief))));

    private static EventProjectionExpression ConstantSourceProjection(string label) =>
        EventProjectionExpression.Default
            .WithFields([new EventProjectionField("TargetId", "tg")])
            .WithIncludes(
            [
                new EventProjectionInclude(
                    EventProjectionConstantIntrinsics.Value,
                    "label",
                    new EventProjectionArgument(
                        EventProjectionConstantIntrinsics.ArgumentName,
                        FilterValue.From(label) with { ParameterKey = "p0" })),
            ]);

    private static EventProjectionExpression ConstantFinalProjection() =>
        EventProjectionExpression.Default.WithFields(
        [
            new EventProjectionField(ProjectedEventPaths.Field("tg"), "TargetId"),
            new EventProjectionField(ProjectedEventPaths.Context("label"), "Label"),
        ]);

    private static SyntaxTree EventTree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record ContextProjectionEvent(
                Guid EventId,
                Guid TargetId,
                Guid SourceId,
                int PluginId,
                int ContentId,
                double Duration) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static object Event(
        Type eventType,
        Guid targetId,
        Guid sourceId,
        int pluginId,
        int contentId,
        double duration) =>
        Activator.CreateInstance(
            eventType,
            Guid.NewGuid(),
            targetId,
            sourceId,
            pluginId,
            contentId,
            duration)!;

    private static string Subject(string assemblyName) =>
        GeneratedModeMatrixSupport.Subject(EventTypeName, assemblyName);

    private static string ProjectedSubject(string assemblyName) =>
        GeneratedModeMatrixSupport.Subject(typeof(ProjectedEvent).FullName!, assemblyName);

    private enum Profession
    {
        Thief,
        Warrior,
    }

    private sealed record Player(Guid Id, Profession Profession);

    private sealed class CombatContext
    {
        private readonly Dictionary<Guid, Player> _players;

        public CombatContext(params Player[] players)
        {
            _players = players.ToDictionary(static player => player.Id);
        }

        public Player? GetPlayer(Guid id) =>
            _players.TryGetValue(id, out Player? player) ? player : null;
    }
}

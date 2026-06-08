using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Projection;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratedHotTierMatrixTests
{
    private const string EventTypeName = "Plugin.Events.PluginOwnedEvent";

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public async Task GeneratedDefaultProjectionIncludesScalarArrays(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Hot.MatrixDefault." + mode;
        EventProjectionExpression projection = EventProjectionExpression.Default;
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.ProjectionEntry(Subject(assemblyName), projection));

        CompiledProjection<object> compiled = ProjectionCompiler.Compile<object>(
            context.EventType,
            projection,
            GeneratedModeMatrixSupport.RejectInclude,
            GeneratedModeMatrixSupport.ProjectionOptions(mode));
        ProjectedEvent projected = await compiled.ProjectAsync(
            Event(context.EventType, characterId: 7, tokens: [100, 200]),
            new object(),
            CancellationToken.None);

        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, compiled.IsTiered);
        Assert.True(projected.TryGetField("Tokens", out var tokens));
        Assert.Equal(ProjectedEventValueKind.Array, tokens.Kind);
        Assert.Equal([100L, 200L], tokens.Values.Select(static item => item.Integer).ToArray());
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public async Task GeneratedPipelineRunsEveryStage(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Hot.MatrixPipeline." + mode;
        FilterExpression sourceFilter = FilterExpression.Compare(
            "CharacterId",
            FilterOperator.GreaterThanOrEqual,
            FilterValue.From(7L));
        EventProjectionExpression firstProjection = EventProjectionExpression.Select(
            "CharacterId",
            "Tokens");
        FilterExpression projectedFilter = FilterExpression.Contains(
            ProjectedEventPaths.Field("Tokens"),
            FilterValue.From(2L));
        EventProjectionExpression finalProjection = EventProjectionExpression.Default.WithFields(
            [new EventProjectionField(ProjectedEventPaths.Field("CharacterId"), "CharacterId")]);
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendSourceFilter(sourceFilter)
            .AppendProjection(firstProjection)
            .AppendFilter(projectedFilter)
            .AppendProjection(finalProjection);
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), sourceFilter),
            GeneratedModeMatrixSupport.ProjectionEntry(Subject(assemblyName), firstProjection),
            GeneratedModeMatrixSupport.FilterEntry(typeof(ProjectedEvent).AssemblyQualifiedName!, projectedFilter),
            GeneratedModeMatrixSupport.ProjectionEntry(typeof(ProjectedEvent).AssemblyQualifiedName!, finalProjection));

        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            context.EventType,
            pipeline,
            GeneratedModeMatrixSupport.RejectInclude,
            GeneratedModeMatrixSupport.PipelineOptions(mode));

        ProjectedEvent? rejectedSource = await compiled.ProjectAsync(
            Event(context.EventType, characterId: 6, tokens: [2]),
            new object(),
            CancellationToken.None);
        ProjectedEvent? rejectedProjected = await compiled.ProjectAsync(
            Event(context.EventType, characterId: 7, tokens: [3]),
            new object(),
            CancellationToken.None);
        ProjectedEvent? accepted = await compiled.ProjectAsync(
            Event(context.EventType, characterId: 7, tokens: [2]),
            new object(),
            CancellationToken.None);

        Assert.Null(rejectedSource);
        Assert.Null(rejectedProjected);
        Assert.NotNull(accepted);
        Assert.Equal(7, accepted!.Field("CharacterId").Integer);
    }

    private static GeneratedModeContext LoadContext(
        GeneratedExecutionMode mode,
        string assemblyName,
        params HotCompilationManifestEntry[] entries) =>
        GeneratedModeMatrixSupport.LoadContext(
            mode,
            assemblyName,
            HotProviderPluginEventSource.Tree(),
            EventTypeName,
            "generated hot matrix",
            entries);

    private static string Subject(string assemblyName) =>
        GeneratedModeMatrixSupport.Subject(EventTypeName, assemblyName);

    private static object Event(Type eventType, long characterId, int[] tokens)
    {
        Type skillRef = eventType.Assembly.GetType("Plugin.Events.SkillRef", throwOnError: true)!;
        Type eventKind = eventType.Assembly.GetType("Plugin.Events.PluginEventKind", throwOnError: true)!;
        return Activator.CreateInstance(
            eventType,
            Guid.NewGuid(),
            characterId,
            Activator.CreateInstance(skillRef, 10, 1),
            Enum.ToObject(eventKind, 1),
            tokens)!;
    }
}

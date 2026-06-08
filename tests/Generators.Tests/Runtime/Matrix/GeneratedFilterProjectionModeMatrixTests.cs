using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratedFilterProjectionModeMatrixTests
{
    private const string EventTypeName = "Plugin.Events.MatrixEvent";

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void CompositeScalarFilterReturnsSameMatches(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.FilterComposite." + mode;
        FilterExpression filter = FilterExpression.And(
            FilterExpression.Compare("CharacterId", FilterOperator.GreaterThanOrEqual, FilterValue.From(7L)),
            FilterExpression.Compare("Active", FilterOperator.Equal, FilterValue.From(true)),
            FilterExpression.Compare("Score", FilterOperator.LessThan, FilterValue.From(10.5)));
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), filter));

        CompiledKernel kernel = FilterCompiler.Compile(
            context.EventType,
            filter,
            GeneratedModeMatrixSupport.FilterOptions(mode));

        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, kernel.IsTiered);
        Assert.True(kernel.Matches(Event(context.EventType, 7, 100, 1, active: true, "alpha", [1], 9.5)));
        Assert.False(kernel.Matches(Event(context.EventType, 6, 100, 1, active: true, "alpha", [1], 9.5)));
        Assert.False(kernel.Matches(Event(context.EventType, 7, 100, 1, active: false, "alpha", [1], 9.5)));
        Assert.False(kernel.Matches(Event(context.EventType, 7, 100, 1, active: true, "alpha", [1], 11.0)));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void InAndContainsFilterReturnsSameMatches(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.FilterInContains." + mode;
        FilterExpression filter = FilterExpression.And(
            FilterExpression.In("Name", [FilterValue.From("alpha"), FilterValue.Null]),
            FilterExpression.Contains("Tokens", FilterValue.From(2L)));
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), filter));

        CompiledKernel kernel = FilterCompiler.Compile(
            context.EventType,
            filter,
            GeneratedModeMatrixSupport.FilterOptions(mode));

        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, kernel.IsTiered);
        Assert.True(kernel.Matches(Event(context.EventType, 7, 100, 1, active: true, "alpha", [1, 2], 1)));
        Assert.True(kernel.Matches(Event(context.EventType, 7, 100, 1, active: true, null, [2], 1)));
        Assert.False(kernel.Matches(Event(context.EventType, 7, 100, 1, active: true, "beta", [2], 1)));
        Assert.False(kernel.Matches(Event(context.EventType, 7, 100, 1, active: true, "alpha", [3], 1)));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void AndFilterRejectsBeforeOversizedContains(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.FilterAndOrder." + mode;
        FilterExpression filter = FilterExpression.And(
            FilterExpression.Contains("Tokens", FilterValue.From(1L)),
            FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(-1L)));
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), filter));

        CompiledKernel kernel = FilterCompiler.Compile(
            context.EventType,
            filter,
            GeneratedModeMatrixSupport.FilterOptions(mode));

        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, kernel.IsTiered);
        Assert.False(kernel.Matches(Event(context.EventType, 7, 0, 1, active: true, "alpha", new int[257], 1)));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void ParameterizedFilterBindsRuntimeValues(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.ParameterizedFilter." + mode;
        FilterExpression manifestFilter = ItemIdFilter(7);
        FilterExpression runtimeFilter = ItemIdFilter(9);
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), manifestFilter));

        CompiledKernel kernel = FilterCompiler.Compile(
            context.EventType,
            runtimeFilter,
            GeneratedModeMatrixSupport.FilterOptions(mode));

        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, kernel.IsTiered);
        Assert.True(kernel.Matches(Event(context.EventType, 7, 9, 1, active: true, "alpha", [1], 1)));
        Assert.False(kernel.Matches(Event(context.EventType, 7, 7, 1, active: true, "alpha", [1], 1)));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public async Task ParameterizedProjectionBindsRuntimeIncludeValues(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.ParameterizedProjection." + mode;
        EventProjectionExpression manifestProjection = Projection(limit: 3);
        EventProjectionExpression runtimeProjection = Projection(limit: 5);
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.ProjectionEntry(Subject(assemblyName), manifestProjection));

        CompiledProjection<object> compiled = ProjectionCompiler.Compile<object>(
            context.EventType,
            runtimeProjection,
            EchoLimit,
            GeneratedModeMatrixSupport.ProjectionOptions(mode));
        ProjectedEvent projected = await compiled.ProjectAsync(
            Event(context.EventType, 7, 9, 1, active: true, "alpha", [1], 1),
            new object(),
            CancellationToken.None);

        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, compiled.IsTiered);
        Assert.Equal(9, projected.Field("ItemId").Integer);
        Assert.Equal(5, projected.ContextValue("limit").Integer);
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public async Task ParameterizedPipelineBindsRuntimeSourceFilterValues(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.ParameterizedPipeline." + mode;
        FilterExpression manifestFilter = ItemIdFilter(7);
        FilterExpression runtimeFilter = ItemIdFilter(9);
        EventProjectionExpression projection = EventProjectionExpression.Select("ItemId");
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendSourceFilter(runtimeFilter)
            .AppendProjection(projection);
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), manifestFilter),
            GeneratedModeMatrixSupport.ProjectionEntry(Subject(assemblyName), projection));

        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            context.EventType,
            pipeline,
            GeneratedModeMatrixSupport.RejectInclude,
            GeneratedModeMatrixSupport.PipelineOptions(mode));

        ProjectedEvent? accepted = await compiled.ProjectAsync(
            Event(context.EventType, 7, 9, 1, active: true, "alpha", [1], 1),
            new object(),
            CancellationToken.None);
        ProjectedEvent? rejected = await compiled.ProjectAsync(
            Event(context.EventType, 7, 7, 1, active: true, "alpha", [1], 1),
            new object(),
            CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Equal(9, accepted!.Field("ItemId").Integer);
        Assert.Null(rejected);
    }

    private static GeneratedModeContext LoadContext(
        GeneratedExecutionMode mode,
        string assemblyName,
        params HotCompilationManifestEntry[] entries) =>
        GeneratedModeMatrixSupport.LoadContext(
            mode,
            assemblyName,
            EventTree(),
            EventTypeName,
            "generated filter/projection matrix",
            entries);

    private static SyntaxTree EventTree() =>
        CSharpSyntaxTree.ParseText("""
            #nullable enable
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record MatrixEvent(
                Guid EventId,
                long CharacterId,
                long ItemId,
                int Quantity,
                bool Active,
                string? Name,
                int[] Tokens,
                double Score) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static string Subject(string assemblyName) =>
        GeneratedModeMatrixSupport.Subject(EventTypeName, assemblyName);

    private static FilterExpression ItemIdFilter(long itemId) =>
        FilterExpression.Compare(
            "ItemId",
            FilterOperator.Equal,
            FilterValue.From(itemId) with { ParameterKey = "p0" });

    private static EventProjectionExpression Projection(long limit) =>
        EventProjectionExpression
            .Select("ItemId")
            .WithIncludes(
            [
                new EventProjectionInclude(
                    "test.limit",
                    "limit",
                    new EventProjectionArgument(
                        "limit",
                        FilterValue.From(limit) with { ParameterKey = "p0" })),
            ]);

    private static CompiledProjection<object>.IncludeProjector EchoLimit(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        int limit = ProjectionIncludeArguments.RequiredInt(include, "limit");
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(limit)));
    }

    private static object Event(
        Type eventType,
        long characterId,
        long itemId,
        int quantity,
        bool active,
        string? name,
        int[] tokens,
        double score) =>
        Activator.CreateInstance(
            eventType,
            Guid.NewGuid(),
            characterId,
            itemId,
            quantity,
            active,
            name,
            tokens,
            score)!;
}

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratedNestedAccessModeMatrixTests
{
    private const string EventTypeName = "Plugin.Events.PlayerMovedEvent";

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void NestedArrayContainsReturnsFalseWhenParentIsNull(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.NestedContains." + mode;
        FilterExpression filter = FilterExpression.Contains(
            "Location.Tags",
            FilterValue.From("rare"));
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), filter));

        CompiledKernel kernel = FilterCompiler.Compile(
            context.EventType,
            filter,
            GeneratedModeMatrixSupport.FilterOptions(mode));

        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, kernel.IsTiered);
        Assert.False(kernel.Matches(Event(context.EventType, location: null)));
        Assert.False(kernel.Matches(Event(context.EventType, Location(context.EventType, "AT", 10, ["common"]))));
        Assert.True(kernel.Matches(Event(context.EventType, Location(context.EventType, "AT", 10, ["rare"]))));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void OrFilterReturnsSameMatchesWhenNestedParentIsNull(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.NestedOr." + mode;
        FilterExpression filter = FilterExpression.Or(
            FilterExpression.Compare("Location.Country", FilterOperator.Equal, FilterValue.From("AT")),
            FilterExpression.Compare("Location.Score", FilterOperator.GreaterThan, FilterValue.From(80L)));
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), filter));

        CompiledKernel kernel = FilterCompiler.Compile(
            context.EventType,
            filter,
            GeneratedModeMatrixSupport.FilterOptions(mode));

        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, kernel.IsTiered);
        Assert.False(kernel.Matches(Event(context.EventType, location: null)));
        Assert.False(kernel.Matches(Event(context.EventType, Location(context.EventType, "DE", 10, ["common"]))));
        Assert.True(kernel.Matches(Event(context.EventType, Location(context.EventType, "AT", 10, ["common"]))));
        Assert.True(kernel.Matches(Event(context.EventType, Location(context.EventType, "DE", 90, ["common"]))));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void NestedNullParentMatchesCompareInAndExistsSemantics(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.NestedNullSemantics." + mode;
        FilterExpression notEqual = FilterExpression.Compare(
            "Location.Country",
            FilterOperator.NotEqual,
            FilterValue.From("AT"));
        FilterExpression inNull = FilterExpression.In("Location.Country", [FilterValue.Null]);
        FilterExpression exists = FilterExpression.Exists("Location.Score");
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), notEqual),
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), inNull),
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), exists));

        CompiledKernel notEqualKernel = FilterCompiler.Compile(
            context.EventType,
            notEqual,
            GeneratedModeMatrixSupport.FilterOptions(mode));
        CompiledKernel inNullKernel = FilterCompiler.Compile(
            context.EventType,
            inNull,
            GeneratedModeMatrixSupport.FilterOptions(mode));
        CompiledKernel existsKernel = FilterCompiler.Compile(
            context.EventType,
            exists,
            GeneratedModeMatrixSupport.FilterOptions(mode));
        object nullParent = Event(context.EventType, location: null);
        object presentParent = Event(context.EventType, Location(context.EventType, "DE", 10, ["common"]));

        Assert.True(notEqualKernel.Matches(nullParent));
        Assert.True(inNullKernel.Matches(nullParent));
        Assert.False(existsKernel.Matches(nullParent));
        Assert.False(inNullKernel.Matches(presentParent));
        Assert.True(existsKernel.Matches(presentParent));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public async Task NestedProjectionWritesNullWhenParentIsNull(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.NestedProjection." + mode;
        EventProjectionExpression projection = EventProjectionExpression.Select(
            "Location.Country",
            "Location.Score");
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.ProjectionEntry(Subject(assemblyName), projection));

        CompiledProjection<object> compiled = ProjectionCompiler.Compile<object>(
            context.EventType,
            projection,
            GeneratedModeMatrixSupport.RejectInclude,
            GeneratedModeMatrixSupport.ProjectionOptions(mode));
        ProjectedEvent nullParent = await compiled.ProjectAsync(
            Event(context.EventType, location: null),
            new object(),
            CancellationToken.None);
        ProjectedEvent presentParent = await compiled.ProjectAsync(
            Event(context.EventType, Location(context.EventType, "AT", 7, ["rare"])),
            new object(),
            CancellationToken.None);

        Assert.Equal(mode == GeneratedExecutionMode.Interpreted, compiled.IsTiered);
        Assert.Equal(ProjectedEventValueKind.Null, nullParent.Field("Location.Country").Kind);
        Assert.Equal(ProjectedEventValueKind.Null, nullParent.Field("Location.Score").Kind);
        Assert.Equal("AT", presentParent.Field("Location.Country").String);
        Assert.Equal(7, presentParent.Field("Location.Score").Integer);
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
            "generated nested access matrix",
            entries);

    private static SyntaxTree EventTree() =>
        CSharpSyntaxTree.ParseText("""
            #nullable enable
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record PlayerLocation(string Country, int Score, string[] Tags);
            public sealed record PlayerMovedEvent(
                Guid EventId,
                PlayerLocation Location) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static string Subject(string assemblyName) =>
        GeneratedModeMatrixSupport.Subject(EventTypeName, assemblyName);

    private static object Event(Type eventType, object? location) =>
        Activator.CreateInstance(eventType, Guid.NewGuid(), location)!;

    private static object Location(Type eventType, string country, int score, string[] tags)
    {
        Type locationType = eventType.Assembly.GetType("Plugin.Events.PlayerLocation", throwOnError: true)!;
        return Activator.CreateInstance(locationType, country, score, tags)!;
    }
}

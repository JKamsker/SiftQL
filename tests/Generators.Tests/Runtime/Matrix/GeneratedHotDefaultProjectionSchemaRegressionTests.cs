using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class GeneratedHotDefaultProjectionSchemaRegressionTests
{
    [Fact]
    public async Task GeneratedHotDefaultProjectionDoesNotHideRuntimeSchemaExpansion()
    {
        const string eventTypeName = "Plugin.Events.MovedEvent";
        const string assemblyName = "Plugin.Hot.DefaultProjectionExpansion";
        EventProjectionExpression projection = EventProjectionExpression.Default;
        using GeneratedModeContext context = GeneratedModeMatrixSupport.LoadContext(
            GeneratedExecutionMode.GeneratedHot,
            assemblyName,
            CSharpSyntaxTree.ParseText("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed class ManualPoint
                {
                    public int X { get; init; }
                }

                public sealed record Location(ManualPoint Point);

                public sealed record MovedEvent(
                    Guid EventId,
                    Location Location) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)),
            eventTypeName,
            "generated hot default projection expansion",
            GeneratedModeMatrixSupport.ProjectionEntry(
                GeneratedModeMatrixSupport.Subject(eventTypeName, assemblyName),
                projection));
        Type pointType = context.Assembly.GetType("Plugin.Events.ManualPoint", throwOnError: true)!;
        FilterSchema.RegisterValueObject(pointType);
        CompiledProjection<object> compiled = ProjectionCompiler.Compile<object>(
            context.EventType,
            projection,
            GeneratedModeMatrixSupport.RejectInclude,
            GeneratedModeMatrixSupport.ProjectionOptions(GeneratedExecutionMode.GeneratedHot));

        ProjectedEvent projected = await compiled.ProjectAsync(
            Event(context.EventType, pointType),
            new object(),
            CancellationToken.None);

        Assert.True(projected.TryGetField("Location.Point.X", out ProjectedEventValue x));
        Assert.Equal(42, x.Integer);
    }

    [Fact]
    public async Task GeneratedHotDefaultProjectionKeepsRealEventMetadataNamedFields()
    {
        const string eventTypeName = "Plugin.Events.MetadataNamedEvent";
        const string assemblyName = "Plugin.Hot.DefaultProjectionMetadataNames";
        EventProjectionExpression projection = EventProjectionExpression.Default;
        using GeneratedModeContext context = GeneratedModeMatrixSupport.LoadContext(
            GeneratedExecutionMode.GeneratedHot,
            assemblyName,
            CSharpSyntaxTree.ParseText("""
                using SiftQL;

                namespace Plugin.Events;

                public sealed record MetadataNamedEvent(
                    string EventType,
                    string EventName,
                    int Value) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)),
            eventTypeName,
            "generated hot default projection metadata fields",
            GeneratedModeMatrixSupport.ProjectionEntry(
                GeneratedModeMatrixSupport.Subject(eventTypeName, assemblyName),
                projection));

        CompiledProjection<object> compiled = ProjectionCompiler.Compile<object>(
            context.EventType,
            projection,
            GeneratedModeMatrixSupport.RejectInclude,
            GeneratedModeMatrixSupport.ProjectionOptions(GeneratedExecutionMode.GeneratedHot));
        ProjectedEvent projected = await compiled.ProjectAsync(
            Activator.CreateInstance(context.EventType, "payload-type", "payload-name", 7)!,
            new object(),
            CancellationToken.None);

        Assert.False(compiled.IsTiered);
        Assert.Equal("payload-type", projected.Field("EventType").String);
        Assert.Equal("payload-name", projected.Field("EventName").String);
        Assert.Equal(7, projected.Field("Value").Integer);
    }

    [Fact]
    public async Task GeneratedHotDefaultProjectionExcludesCollectionDerivedFields()
    {
        const string eventTypeName = "Plugin.Events.InventoryEvent";
        const string assemblyName = "Plugin.Hot.DefaultProjectionCollections";
        EventProjectionExpression projection = EventProjectionExpression.Default;
        using GeneratedModeContext context = GeneratedModeMatrixSupport.LoadContext(
            GeneratedExecutionMode.GeneratedHot,
            assemblyName,
            CSharpSyntaxTree.ParseText("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed record Item(int Quantity);

                public sealed record InventoryEvent(
                    Guid EventId,
                    Item[] Items) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)),
            eventTypeName,
            "generated hot default projection collection fields",
            GeneratedModeMatrixSupport.ProjectionEntry(
                GeneratedModeMatrixSupport.Subject(eventTypeName, assemblyName),
                projection));

        CompiledProjection<object> compiled = ProjectionCompiler.Compile<object>(
            context.EventType,
            projection,
            GeneratedModeMatrixSupport.RejectInclude,
            GeneratedModeMatrixSupport.ProjectionOptions(GeneratedExecutionMode.GeneratedHot));
        ProjectedEvent projected = await compiled.ProjectAsync(
            InventoryEvent(context.EventType),
            new object(),
            CancellationToken.None);

        Assert.False(compiled.IsTiered);
        Assert.True(projected.TryGetField("EventId", out _));
        Assert.False(projected.TryGetField("Items.Quantity", out _));
    }

    [Fact]
    public async Task GeneratedHotDefaultProjectionExcludesNestedSubjectTypesMetadata()
    {
        const string eventTypeName = "Plugin.Events.CombatEvent";
        const string assemblyName = "Plugin.Hot.DefaultProjectionSubjectTypes";
        EventProjectionExpression projection = EventProjectionExpression.Default;
        using GeneratedModeContext context = GeneratedModeMatrixSupport.LoadContext(
            GeneratedExecutionMode.GeneratedHot,
            assemblyName,
            CSharpSyntaxTree.ParseText("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public abstract record Entity(string[] SubjectTypes);
                public sealed record Player(string[] SubjectTypes) : Entity(SubjectTypes);
                public sealed record Monster(string[] SubjectTypes) : Entity(SubjectTypes);

                public sealed record CombatEvent(
                    Guid EventId,
                    Entity Defender) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)),
            eventTypeName,
            "generated hot default projection nested subjectTypes",
            GeneratedModeMatrixSupport.ProjectionEntry(
                GeneratedModeMatrixSupport.Subject(eventTypeName, assemblyName),
                projection));

        CompiledProjection<object> compiled = ProjectionCompiler.Compile<object>(
            context.EventType,
            projection,
            GeneratedModeMatrixSupport.RejectInclude,
            GeneratedModeMatrixSupport.ProjectionOptions(GeneratedExecutionMode.GeneratedHot));
        ProjectedEvent projected = await compiled.ProjectAsync(
            CombatEvent(context.EventType),
            new object(),
            CancellationToken.None);

        Assert.False(compiled.IsTiered);
        Assert.True(projected.TryGetField("EventId", out _));
        Assert.False(projected.TryGetField("Defender.SubjectTypes", out _));
        Assert.False(projected.TryGetField("Defender.subjectTypes", out _));
    }

    private static object Event(Type eventType, Type pointType)
    {
        Type locationType = eventType.Assembly.GetType("Plugin.Events.Location", throwOnError: true)!;
        object point = Activator.CreateInstance(pointType)!;
        pointType.GetProperty("X")!.SetValue(point, 42);
        object location = Activator.CreateInstance(locationType, point)!;
        return Activator.CreateInstance(eventType, Guid.NewGuid(), location)!;
    }

    private static object InventoryEvent(Type eventType)
    {
        Type itemType = eventType.Assembly.GetType("Plugin.Events.Item", throwOnError: true)!;
        Array items = Array.CreateInstance(itemType, 1);
        items.SetValue(Activator.CreateInstance(itemType, 7), 0);
        return Activator.CreateInstance(eventType, Guid.NewGuid(), items)!;
    }

    private static object CombatEvent(Type eventType)
    {
        Type monsterType = eventType.Assembly.GetType("Plugin.Events.Monster", throwOnError: true)!;
        object defender = Activator.CreateInstance(monsterType, [Array.Empty<string>()])!;
        return Activator.CreateInstance(eventType, Guid.NewGuid(), defender)!;
    }
}

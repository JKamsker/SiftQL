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

    private static object Event(Type eventType, Type pointType)
    {
        Type locationType = eventType.Assembly.GetType("Plugin.Events.Location", throwOnError: true)!;
        object point = Activator.CreateInstance(pointType)!;
        pointType.GetProperty("X")!.SetValue(point, 42);
        object location = Activator.CreateInstance(locationType, point)!;
        return Activator.CreateInstance(eventType, Guid.NewGuid(), location)!;
    }
}

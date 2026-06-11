using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionCompilerKeyRegressionTests
{
    [Fact]
    public void ProjectionKeysSeparateEqualFieldShapesAcrossSubjectTypes()
    {
        EventProjectionExpression projection = EventProjectionExpression.Select(nameof(FirstValueEvent.Value));
        CompiledProjection<object> first = ProjectionCompiler.Compile<object>(
            typeof(FirstValueEvent),
            projection,
            RejectInclude,
            ProjectionCompilerOptions.Immediate);
        CompiledProjection<object> second = ProjectionCompiler.Compile<object>(
            typeof(SecondValueEvent),
            projection,
            RejectInclude,
            ProjectionCompilerOptions.Immediate);
        var accumulator = new ProjectionMatchAccumulator<CompiledProjection<object>>();

        accumulator.Add("first", first.Key, first);
        accumulator.Add("second", second.Key, second);

        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(2, accumulator.GroupCount);
    }

    [Fact]
    public void ProjectionAndPipelineKeysSeparateClosedGenericIncludeCompilers()
    {
        EventProjectionExpression projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude("test.marker", "Marker"),
        ]);
        CompiledProjection<object> first = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            projection,
            CompileMarker<FirstMarker>,
            ProjectionCompilerOptions.Immediate);
        CompiledProjection<object> second = ProjectionCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            projection,
            CompileMarker<SecondMarker>,
            ProjectionCompilerOptions.Immediate);
        EventPipelineExpression pipeline = EventPipelineExpression.Default.AppendProjection(projection);
        CompiledEventPipeline<object> firstPipeline = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            CompileMarker<FirstMarker>,
            EventPipelineCompilerOptions.Immediate);
        CompiledEventPipeline<object> secondPipeline = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            CompileMarker<SecondMarker>,
            EventPipelineCompilerOptions.Immediate);

        Assert.NotEqual(first.Key, second.Key);
        Assert.NotEqual(firstPipeline.Key, secondPipeline.Key);
    }

    [Fact]
    public void ProjectionAndPipelineKeysSeparateSchemaVersionsWithEqualFieldShapes()
    {
        EventProjectionExpression projection =
            EventProjectionExpression.Select("GeneratedFlag");
        EventPipelineExpression pipeline =
            EventPipelineExpression.Default.AppendProjection(projection);

        GeneratedFilterSchemaRegistry.Register(
            typeof(SchemaVersionKeyEvent).Assembly,
            FalseSchemaProvider);
        CompiledProjection<object> first = ProjectionCompiler.Compile<object>(
            typeof(SchemaVersionKeyEvent),
            projection,
            RejectInclude,
            ProjectionCompilerOptions.Immediate);
        CompiledEventPipeline<object> firstPipeline = EventPipelineCompiler.Compile<object>(
            typeof(SchemaVersionKeyEvent),
            pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);

        GeneratedFilterSchemaRegistry.Register(
            typeof(SchemaVersionKeyEvent).Assembly,
            TrueSchemaProvider);
        CompiledProjection<object> second = ProjectionCompiler.Compile<object>(
            typeof(SchemaVersionKeyEvent),
            projection,
            RejectInclude,
            ProjectionCompilerOptions.Immediate);
        CompiledEventPipeline<object> secondPipeline = EventPipelineCompiler.Compile<object>(
            typeof(SchemaVersionKeyEvent),
            pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);
        var accumulator = new ProjectionMatchAccumulator<CompiledProjection<object>>();

        accumulator.Add("first", first.Key, first);
        accumulator.Add("second", second.Key, second);

        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(2, accumulator.GroupCount);
        Assert.NotEqual(firstPipeline.Key, secondPipeline.Key);
    }

    private static CompiledProjection<object>.IncludeProjector CompileMarker<TMarker>(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            static (_, _, _) => ValueTask.FromResult(
                ProjectedEventValue.FromScalar(typeof(TMarker).Name)));
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private static bool FalseSchemaProvider(Type candidate, out FilterSchema? schema) =>
        SchemaProvider(candidate, generatedFlag: false, out schema);

    private static bool TrueSchemaProvider(Type candidate, out FilterSchema? schema) =>
        SchemaProvider(candidate, generatedFlag: true, out schema);

    private static bool SchemaProvider(
        Type candidate,
        bool generatedFlag,
        out FilterSchema? schema)
    {
        if (candidate != typeof(SchemaVersionKeyEvent))
        {
            schema = null;
            return false;
        }

        schema = GeneratedFilterSchemaRegistry.Create(
            candidate,
            [
                TestFilterHelpers.ReservedField(
                    "subjectType",
                    static subject => subject.GetType().FullName ?? subject.GetType().Name),
                TestFilterHelpers.ReservedField(
                    "subjectName",
                    static subject => subject.GetType().Name),
                new FilterField(
                    "GeneratedFlag",
                    typeof(bool),
                    FilterFieldKind.Scalar,
                    _ => generatedFlag,
                    new FilterScalarAccessor(
                        FilterScalarKind.Boolean,
                        requiredBoolean: _ => generatedFlag),
                    ProjectionAccessor: _ => ProjectedEventValue.FromScalar(generatedFlag)),
            ]);
        return true;
    }

    private sealed record FirstValueEvent(int Value) : IFilterSubject;

    private sealed record SecondValueEvent(int Value) : IFilterSubject;

    private sealed record SchemaVersionKeyEvent : IFilterSubject;

    private sealed class FirstMarker;

    private sealed class SecondMarker;
}

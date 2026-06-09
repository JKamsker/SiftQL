using System.Reflection;
using System.Reflection.Emit;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaFallbackRegressionTests
{
    [Fact]
    public void RegisterValueObjectInvalidatesPreviouslyBuiltFallbackSchemas()
    {
        FilterSchema beforeRegistration = FilterSchema.For(typeof(LateRegisteredLocationEvent));
        Assert.False(beforeRegistration.TryGetField("Location.MapId", out _));

        FilterSchema.RegisterValueObject<LateRegisteredLocation>();
        FilterSchema afterRegistration = FilterSchema.For(typeof(LateRegisteredLocationEvent));

        Assert.True(afterRegistration.TryGetField("Location.MapId", out _));
    }

    [Fact]
    public void NullableApprovedValueObjectDoesNotCrashFallbackSchema()
    {
        FilterSchema.RegisterValueObject<MapLocation>();

        FilterSchema schema = FilterSchema.For(typeof(NullableLocationEvent));

        Assert.True(schema.TryGetField(nameof(NullableLocationEvent.Location), out _));
        Assert.False(schema.TryGetField("Location.MapId", out _));
    }

    [Fact]
    public void NullableReferenceValueObjectExposesNullSafeNestedFallbackFields()
    {
        FilterSchema.RegisterValueObject<ReferenceLocation>();

        FilterSchema schema = FilterSchema.For(typeof(NullableReferenceLocationEvent));

        Assert.True(schema.TryGetField(nameof(NullableReferenceLocationEvent.Location), out _));
        // Nullable reference owners expand because nested accessors
        // null-propagate; only Nullable<T> owners stay unexpanded.
        Assert.True(schema.TryGetField("Location.MapId", out _));
    }

    [Fact]
    public void RegisterGeneratedProviderInvalidatesSchemaAndFilterCaches()
    {
        Type subjectType = CreateGeneratedProviderCacheSubjectType();
        var filter = FilterExpression.Compare(
            "Flag",
            FilterOperator.Equal,
            FilterValue.From(true));
        object subject = Activator.CreateInstance(subjectType)!;

        FilterSchema beforeSchema = FilterSchema.For(subjectType);
        CompiledKernel beforeKernel = FilterCompiler.Compile(
            subjectType,
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(beforeSchema.TryGetField("Flag", out _));
        Assert.False(beforeSchema.TryGetField("GeneratedOnly", out _));
        Assert.False(beforeKernel.Matches(subject));

        GeneratedFilterSchemaRegistry.Register(subjectType.Assembly, Provider);

        FilterSchema afterSchema = FilterSchema.For(subjectType);
        CompiledKernel afterKernel = FilterCompiler.Compile(
            subjectType,
            filter,
            FilterCompilerOptions.Immediate);

        Assert.True(afterSchema.TryGetField("GeneratedOnly", out _));
        Assert.True(afterKernel.Matches(subject));

        bool Provider(Type candidate, out FilterSchema? schema)
        {
            if (candidate != subjectType)
            {
                schema = null;
                return false;
            }

            schema = GeneratedFilterSchemaRegistry.Create(
                candidate,
                [
                    ReservedField("subjectType", static subject => subject.GetType().FullName ?? subject.GetType().Name),
                    ReservedField("subjectName", static subject => subject.GetType().Name),
                    new FilterField(
                        "Flag",
                        typeof(bool),
                        FilterFieldKind.Scalar,
                        static _ => true,
                        new FilterScalarAccessor(FilterScalarKind.Boolean, requiredBoolean: static _ => true)),
                    new FilterField(
                        "GeneratedOnly",
                        typeof(string),
                        FilterFieldKind.Scalar,
                        static _ => "generated",
                        new FilterScalarAccessor(FilterScalarKind.String, text: static _ => "generated")),
                ]);
            return true;
        }
    }

    private static FilterField ReservedField(string name, Func<object, string> value) =>
        new(
            name,
            typeof(string),
            FilterFieldKind.Scalar,
            value,
            new FilterScalarAccessor(FilterScalarKind.String, text: value));

    [Fact]
    public async Task EventPipelineCacheRefreshesWhenValueObjectRegistrationChangesSchema()
    {
        var pipeline = EventPipelineExpression.Default;
        var ev = new PipelineMovedEvent(Guid.NewGuid(), new PipelineLocation(42));
        CompiledEventPipeline<object?> before = CompilePipeline(pipeline);
        ProjectedEvent? beforeProjected = await before.ProjectAsync(ev, null, CancellationToken.None);

        Assert.NotNull(beforeProjected);
        Assert.False(beforeProjected!.TryGetField("Location.MapId", out _));

        FilterSchema.RegisterValueObject<PipelineLocation>();
        CompiledEventPipeline<object?> after = CompilePipeline(pipeline);
        ProjectedEvent? afterProjected = await after.ProjectAsync(ev, null, CancellationToken.None);

        Assert.NotNull(afterProjected);
        Assert.True(afterProjected!.TryGetField("Location.MapId", out var mapId));
        Assert.Equal(42, mapId.Integer);
    }

    private static CompiledEventPipeline<object?> CompilePipeline(EventPipelineExpression pipeline) =>
        EventPipelineCompiler.Compile<object?>(
            typeof(PipelineMovedEvent),
            pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Immediate);

    private static CompiledProjection<object?>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private static Type CreateGeneratedProviderCacheSubjectType()
    {
        string name = "SiftQL.GeneratedProviderCache." + Guid.NewGuid().ToString("N");
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(name),
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(name);
        TypeBuilder type = module.DefineType(
            "Plugin.GeneratedProviderCacheEvent",
            TypeAttributes.Public | TypeAttributes.Class);
        type.AddInterfaceImplementation(typeof(IFilterSubject));

        ConstructorBuilder constructor = type.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        ILGenerator ctor = constructor.GetILGenerator();
        ctor.Emit(OpCodes.Ldarg_0);
        ctor.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctor.Emit(OpCodes.Ret);

        MethodBuilder getter = type.DefineMethod(
            "get_Flag",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(bool),
            Type.EmptyTypes);
        ILGenerator getFlag = getter.GetILGenerator();
        getFlag.Emit(OpCodes.Ldc_I4_0);
        getFlag.Emit(OpCodes.Ret);

        PropertyBuilder property = type.DefineProperty(
            "Flag",
            PropertyAttributes.None,
            typeof(bool),
            Type.EmptyTypes);
        property.SetGetMethod(getter);

        return type.CreateType()!;
    }

    private readonly record struct MapLocation(long MapId, int X, int Y);

    private sealed record NullableLocationEvent(
        Guid EventId,
        MapLocation? Location) : IFilterSubject;

    private sealed record LateRegisteredLocation(long MapId);

    private sealed record LateRegisteredLocationEvent(
        Guid EventId,
        LateRegisteredLocation Location) : IFilterSubject;

    private sealed record ReferenceLocation(long MapId);

    private sealed record NullableReferenceLocationEvent(
        Guid EventId,
        ReferenceLocation? Location) : IFilterSubject;

    private sealed record PipelineLocation(long MapId);

    private sealed record PipelineMovedEvent(
        Guid EventId,
        PipelineLocation Location) : IFilterSubject;
}

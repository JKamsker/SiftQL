using System.Reflection;
using System.Reflection.Emit;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexScalarAccessorRegressionTests
{
    [Fact]
    public void DynamicScalarAccessorDrivesIndexedLookupWhenGetterShapeDiffers()
    {
        Type subjectType = CreateSubjectType();
        object subject = Activator.CreateInstance(subjectType)!;
        GeneratedFilterSchemaRegistry.Register(subjectType.Assembly, Provider);
        var filter = FilterExpression.Compare("Value", FilterOperator.Equal, FilterValue.From(1.0D));
        CompiledKernel kernel = FilterCompiler.Compile(subjectType, filter, FilterCompilerOptions.Immediate);
        var index = new FilterSubscriptionIndex<string>(subjectType);

        index.Add("dynamic", filter);

        Assert.True(kernel.Matches(subject));
        Assert.Equal(["dynamic"], index.SnapshotMatches(subject));

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
                    Reserved("subjectType", static subject => subject.GetType().FullName ?? subject.GetType().Name),
                    Reserved("subjectName", static subject => subject.GetType().Name),
                    new FilterField(
                        "Value",
                        typeof(double),
                        FilterFieldKind.Scalar,
                        static _ => 1L,
                        new FilterScalarAccessor(
                            FilterScalarKind.Number,
                            requiredNumber: static _ => 1.0D),
                        ProjectionAccessor: static _ => ProjectedEventValue.FromScalar(1.0D)),
                ]);
            return true;
        }
    }

    [Fact]
    public void ExactNumericGetterDrivesIndexedLookupWhenScalarAccessorWouldRound()
    {
        const long exact = 9_007_199_254_740_993L;
        Type subjectType = CreateSubjectType();
        object subject = Activator.CreateInstance(subjectType)!;
        GeneratedFilterSchemaRegistry.Register(subjectType.Assembly, Provider);
        var filter = FilterExpression.Compare("Value", FilterOperator.Equal, FilterValue.From(exact));
        CompiledKernel kernel = FilterCompiler.Compile(subjectType, filter, FilterCompilerOptions.Immediate);
        var index = new FilterSubscriptionIndex<string>(subjectType);

        index.Add("exact", filter);

        Assert.True(kernel.Matches(subject));
        Assert.Equal(["exact"], index.SnapshotCandidates(subject));
        Assert.Equal(["exact"], index.SnapshotMatches(subject));

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
                    Reserved("subjectType", static subject => subject.GetType().FullName ?? subject.GetType().Name),
                    Reserved("subjectName", static subject => subject.GetType().Name),
                    new FilterField(
                        "Value",
                        typeof(long),
                        FilterFieldKind.Scalar,
                        static _ => exact,
                        new FilterScalarAccessor(
                            FilterScalarKind.Number,
                            requiredNumber: static _ => (double)exact),
                        ProjectionAccessor: static _ => ProjectedEventValue.FromScalar(exact)),
                ]);
            return true;
        }
    }

    [Fact]
    public void DynamicScalarAccessorDrivesRangeLookupWhenGetterShapeDiffers()
    {
        GeneratedFilterSchemaRegistry.Register(typeof(RangeAccessorSubject).Assembly, Provider);
        var filter = FilterExpression.Compare(
            "Value",
            FilterOperator.GreaterThan,
            FilterValue.From(0L));
        var subject = new RangeAccessorSubject();
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(RangeAccessorSubject),
            filter,
            FilterCompilerOptions.Immediate);
        var index = new FilterSubscriptionIndex<string>(typeof(RangeAccessorSubject));
        var typedIndex = new TypedFilterSubscriptionIndex<string, RangeAccessorSubject>();

        index.Add("dynamic-range", filter);
        typedIndex.Add("dynamic-range", filter);

        Assert.True(kernel.Matches(subject));
        Assert.Equal(1, index.GetStatistics().RangeIndexedCount);
        Assert.Equal(["dynamic-range"], index.SnapshotCandidates(subject));
        Assert.Equal(["dynamic-range"], index.SnapshotMatches(subject));
        Assert.Equal(["dynamic-range"], typedIndex.SnapshotCandidates(subject));
        Assert.Equal(["dynamic-range"], typedIndex.SnapshotMatches(subject));

        static bool Provider(Type candidate, out FilterSchema? schema)
        {
            if (candidate != typeof(RangeAccessorSubject))
            {
                schema = null;
                return false;
            }

            schema = GeneratedFilterSchemaRegistry.Create(
                candidate,
                [
                    Reserved("subjectType", static subject => subject.GetType().FullName ?? subject.GetType().Name),
                    Reserved("subjectName", static subject => subject.GetType().Name),
                    new FilterField(
                        "Value",
                        typeof(long),
                        FilterFieldKind.Scalar,
                        static _ => 1.0D,
                        new FilterScalarAccessor(
                            FilterScalarKind.Number,
                            requiredNumber: static _ => 1.0D),
                        ProjectionAccessor: static _ => ProjectedEventValue.FromScalar(1.0D)),
                ]);
            return true;
        }
    }

    private static FilterField Reserved(string name, Func<object, string> value) =>
        new(
            name,
            typeof(string),
            FilterFieldKind.Scalar,
            value,
            new FilterScalarAccessor(FilterScalarKind.String, text: value));

    private static Type CreateSubjectType()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("SiftQL.DynamicScalarIndex." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule("main");
        TypeBuilder type = module.DefineType(
            "Plugin.DynamicScalarIndexEvent",
            TypeAttributes.Public | TypeAttributes.Class);
        type.AddInterfaceImplementation(typeof(IFilterSubject));
        ConstructorBuilder constructor = type.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        ILGenerator il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
        return type.CreateType()!;
    }

    private sealed record RangeAccessorSubject : IFilterSubject;
}

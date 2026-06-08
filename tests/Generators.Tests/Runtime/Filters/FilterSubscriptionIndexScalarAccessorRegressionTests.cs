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
}

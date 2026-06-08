using System.Reflection;
using System.Reflection.Emit;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexDynamicValueRegressionTests
{
    [Fact]
    public void DynamicProjectedValueFiltersStayUnindexedWhenLiteralKindDiffersFromActualKind()
    {
        Type subjectType = CreateSubjectType();
        object subject = Activator.CreateInstance(subjectType)!;
        GeneratedFilterSchemaRegistry.Register(subjectType.Assembly, Provider);
        var filter = FilterExpression.Compare(
            "Value",
            FilterOperator.Equal,
            FilterValue.From(1L));

        CompiledKernel kernel = FilterCompiler.Compile(
            subjectType,
            filter,
            FilterCompilerOptions.Immediate);
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
                    new FilterField(
                        "Value",
                        typeof(ProjectedEventValue),
                        FilterFieldKind.Scalar,
                        static _ => 1.0d,
                        ProjectionAccessor: static _ => ProjectedEventValue.FromScalar(1.0d)),
                ]);
            return true;
        }
    }

    private static Type CreateSubjectType()
    {
        string name = "SiftQL.DynamicValueIndex." + Guid.NewGuid().ToString("N");
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(name),
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(name);
        TypeBuilder type = module.DefineType(
            "Plugin.DynamicValueIndexEvent",
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

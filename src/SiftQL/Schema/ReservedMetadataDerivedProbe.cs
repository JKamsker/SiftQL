using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SiftQL.Schema;

internal static class ReservedMetadataDerivedProbe
{
    private static readonly AssemblyBuilder s_assembly = AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("SiftQL.ReservedMetadataProbes"),
        AssemblyBuilderAccess.Run);
    private static readonly ModuleBuilder s_module = s_assembly.DefineDynamicModule("SiftQL.ReservedMetadataProbes");
    private static int s_nextId;

    public static bool TryCreate(Type subjectType, [NotNullWhen(true)] out object? probe)
    {
        probe = null;
        if (subjectType.IsSealed || subjectType.IsValueType || subjectType.IsInterface)
            return false;

        try
        {
            ConstructorInfo? constructor = SelectConstructor(subjectType);
            if (constructor is null)
                return false;

            TypeBuilder builder = s_module.DefineType(
                "SiftQL.ReservedMetadataProbe" + Interlocked.Increment(ref s_nextId),
                TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.NotPublic,
                subjectType);
            EmitConstructor(builder, constructor);
            Type probeType = builder.CreateTypeInfo()!.AsType();
            probe = RuntimeHelpers.GetUninitializedObject(probeType);
            return probe is not null;
        }
        catch
        {
            probe = null;
            return false;
        }
    }

    private static ConstructorInfo? SelectConstructor(Type subjectType) =>
        subjectType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(CanCall)
            .OrderBy(static constructor => constructor.GetParameters().Length)
            .FirstOrDefault();

    private static bool CanCall(ConstructorInfo constructor) =>
        (constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly) &&
        constructor.GetParameters().All(static parameter => CanEmitDefault(parameter.ParameterType));

    private static bool CanEmitDefault(Type type) =>
        !type.IsByRef && !type.IsPointer;

    private static void EmitConstructor(TypeBuilder builder, ConstructorInfo baseConstructor)
    {
        ConstructorBuilder constructor = builder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        ILGenerator il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        foreach (ParameterInfo parameter in baseConstructor.GetParameters())
            EmitDefault(il, parameter.ParameterType);
        il.Emit(OpCodes.Call, baseConstructor);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitDefault(ILGenerator il, Type type)
    {
        if (!type.IsValueType)
        {
            il.Emit(OpCodes.Ldnull);
            return;
        }

        LocalBuilder local = il.DeclareLocal(type);
        il.Emit(OpCodes.Ldloca_S, local);
        il.Emit(OpCodes.Initobj, type);
        il.Emit(OpCodes.Ldloc, local);
    }
}

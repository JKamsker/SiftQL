using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Projected;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexVersionRaceRegressionTests
{
    [Fact]
    public async Task SchemaRefreshPublishesRebuiltSnapshotBeforeCurrentVersion()
    {
        Type subjectType = CreateSubjectType();
        object subject = Activator.CreateInstance(subjectType)!;
        var subscription = new BlockingSubscription();
        var index = new FilterSubscriptionIndex<BlockingSubscription>(subjectType);
        index.Add(
            subscription,
            FilterExpression.Compare("Flag", FilterOperator.Equal, FilterValue.From(true)));
        Assert.Empty(index.SnapshotMatches(subject));

        GeneratedFilterSchemaRegistry.Register(subjectType.Assembly, Provider);
        subscription.Arm();
        Task<BlockingSubscription[]> rebuildTask = Task.Run(() => index.SnapshotMatches(subject));
        Assert.True(subscription.WaitUntilBlocked(TimeSpan.FromSeconds(5)));

        Task<BlockingSubscription[]> readerTask = Task.Run(() => index.SnapshotMatches(subject));
        await Task.Delay(50);
        bool completedBeforeRebuild = readerTask.IsCompleted;
        BlockingSubscription[] earlyReaderResult = completedBeforeRebuild
            ? await readerTask
            : [];

        subscription.Release();
        BlockingSubscription[] rebuilt = await rebuildTask.WaitAsync(TimeSpan.FromSeconds(5));
        BlockingSubscription[] reader = completedBeforeRebuild
            ? earlyReaderResult
            : await readerTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(
            completedBeforeRebuild && earlyReaderResult.Length == 0,
            "Reader observed the current schema version with the stale pre-rebuild snapshot.");
        Assert.Equal([subscription], rebuilt);
        Assert.Equal([subscription], reader);

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
                        "Flag",
                        typeof(bool),
                        FilterFieldKind.Scalar,
                        static _ => true,
                        new FilterScalarAccessor(
                            FilterScalarKind.Boolean,
                            requiredBoolean: static _ => true),
                        ProjectionAccessor: static _ => ProjectedEventValue.FromScalar(true)),
                ]);
            return true;
        }
    }

    private static Type CreateSubjectType()
    {
        string name = "SiftQL.Index.VersionRace." + Guid.NewGuid().ToString("N");
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(name),
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(name);
        TypeBuilder type = module.DefineType(
            "Plugin.VersionRaceEvent",
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

    private sealed class BlockingSubscription
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        private int _armed;
        private int _blocked;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public bool WaitUntilBlocked(TimeSpan timeout) => _entered.Wait(timeout);

        public void Release() => _release.Set();

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);

        public override int GetHashCode()
        {
            if (Volatile.Read(ref _armed) != 0 &&
                Interlocked.CompareExchange(ref _blocked, 1, 0) == 0)
            {
                _entered.Set();
                Assert.True(_release.Wait(TimeSpan.FromSeconds(5)));
            }

            return RuntimeHelpers.GetHashCode(this);
        }
    }
}

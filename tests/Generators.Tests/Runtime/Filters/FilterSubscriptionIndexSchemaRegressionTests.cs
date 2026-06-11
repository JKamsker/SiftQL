using System.Reflection;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Index;
using SiftQL.Projected;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSubscriptionIndexSchemaRegressionTests
{
    [Fact]
    public void IndexUsesCurrentSchemaAfterGeneratedProviderRegistration()
    {
        Type subjectType = EmitSubjectType();
        var index = new FilterSubscriptionIndex<string>(subjectType);
        GeneratedFilterSchemaRegistry.Register(subjectType.Assembly, Provider);
        var filter = FilterExpression.Compare(
            "Flag",
            FilterOperator.Equal,
            FilterValue.From(true));
        object subject = Activator.CreateInstance(subjectType)!;

        Assert.True(FilterCompiler.Compile(subjectType, filter).Matches(subject));

        index.Add("sub", filter);

        Assert.Equal(["sub"], index.SnapshotMatches(subject));

        bool Provider(Type type, out FilterSchema? schema)
        {
            if (type != subjectType)
            {
                schema = null;
                return false;
            }

            schema = GeneratedFilterSchemaRegistry.Create(
                type,
                [
                    TestFilterHelpers.ReservedField(
                        "subjectType",
                        static subject => subject.GetType().FullName ?? subject.GetType().Name),
                    TestFilterHelpers.ReservedField("subjectName", static subject => subject.GetType().Name),
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

    [Fact]
    public void IndexRebuildsUnindexedKernelsAfterHotProviderChanges()
    {
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        FilterExpression filter = RegionMissingFilter();
        using IDisposable registration = PrecompiledTieredProviderRegistry.Register(
            new AlwaysMatchingHotProvider(typeof(HotIndexedSubject), TestFilterHelpers.Fingerprint(filter)));
        var index = new FilterSubscriptionIndex<string>(typeof(HotIndexedSubject));

        index.Add("sub", filter);

        Assert.Equal(["sub"], index.SnapshotMatches(new HotIndexedSubject("north")));

        registration.Dispose();

        Assert.Empty(index.SnapshotMatches(new HotIndexedSubject("north")));
    }

    [Fact]
    public void IndexRebuildsWhenEnteringNestedIsolatedHotProviderScope()
    {
        using var outer = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        FilterExpression filter = RegionMissingFilter();
        using IDisposable registration = PrecompiledTieredProviderRegistry.Register(
            new AlwaysMatchingHotProvider(typeof(HotIndexedSubject), TestFilterHelpers.Fingerprint(filter)));
        var index = new FilterSubscriptionIndex<string>(typeof(HotIndexedSubject));
        index.Add("sub", filter);
        Assert.Equal(["sub"], index.SnapshotMatches(new HotIndexedSubject("north")));

        using (PrecompiledTieredProviderRegistry.CreateIsolatedScope())
        {
            Assert.False(PrecompiledTieredProviderRegistry.HasProviders);
            Assert.Empty(index.SnapshotMatches(new HotIndexedSubject("north")));
        }

        Assert.Equal(["sub"], index.SnapshotMatches(new HotIndexedSubject("north")));
    }

    [Fact]
    public async Task IndexRebuildsWhenExecutionContextDropsIsolatedHotProviderScope()
    {
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        FilterExpression filter = RegionMissingFilter();
        using IDisposable registration = PrecompiledTieredProviderRegistry.Register(
            new AlwaysMatchingHotProvider(typeof(HotIndexedSubject), TestFilterHelpers.Fingerprint(filter)));
        var index = new FilterSubscriptionIndex<string>(typeof(HotIndexedSubject));
        index.Add("sub", filter);

        Assert.Equal(["sub"], index.SnapshotMatches(new HotIndexedSubject("north")));

        string[] matches = await RunWithoutExecutionContextAsync(
            () => index.SnapshotMatches(new HotIndexedSubject("north")));

        Assert.Empty(matches);
        Assert.Equal(["sub"], index.SnapshotMatches(new HotIndexedSubject("north")));
    }

    private static FilterExpression RegionMissingFilter() =>
        FilterExpression.Not(FilterExpression.Exists(nameof(HotIndexedSubject.Region)));

    private static async Task<T> RunWithoutExecutionContextAsync<T>(Func<T> action)
    {
        Task<T> task;
        using (ExecutionContext.SuppressFlow())
            task = Task.Run(action);
        return await task;
    }

    private static Type EmitSubjectType()
    {
        string assemblyName = "Plugin.Index.Schema." + Guid.NewGuid().ToString("N");
        SyntaxTree tree = CSharpSyntaxTree.ParseText("""
            using SiftQL;

            namespace Plugin.Events;

            public sealed record IndexedEvent : IFilterSubject
            {
                public bool Flag => false;
            }
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, tree);
        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        AssertEx.True(emit.Success, "index schema subject emitted: " + string.Join(" | ", emit.Diagnostics));
        Assembly assembly = Assembly.Load(pe.ToArray());
        return assembly.GetType("Plugin.Events.IndexedEvent", throwOnError: true)!;
    }

    private sealed record HotIndexedSubject(string Region) : IFilterSubject;
}

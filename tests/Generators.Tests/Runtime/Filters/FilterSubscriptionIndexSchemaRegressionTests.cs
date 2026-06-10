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
                    ReservedField("subjectType", static subject => subject.GetType().FullName ?? subject.GetType().Name),
                    ReservedField("subjectName", static subject => subject.GetType().Name),
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
            new AlwaysMatchingHotProvider(typeof(HotIndexedSubject), Fingerprint(filter)));
        var index = new FilterSubscriptionIndex<string>(typeof(HotIndexedSubject));

        index.Add("sub", filter);

        Assert.Equal(["sub"], index.SnapshotMatches(new HotIndexedSubject("north")));

        registration.Dispose();

        Assert.Empty(index.SnapshotMatches(new HotIndexedSubject("north")));
    }

    private static FilterExpression RegionMissingFilter() =>
        FilterExpression.Not(FilterExpression.Exists(nameof(HotIndexedSubject.Region)));

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod(
            "Create",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [expression])!;
    }

    private static FilterField ReservedField(string name, Func<object, string> value) =>
        new(
            name,
            typeof(string),
            FilterFieldKind.Scalar,
            value,
            new FilterScalarAccessor(FilterScalarKind.String, text: value));

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

    private sealed class AlwaysMatchingHotProvider(
        Type subjectType,
        string acceptedFingerprint) : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(
            Type type,
            string fingerprint,
            out Func<object, bool>? predicate)
        {
            if (type == subjectType && string.Equals(fingerprint, acceptedFingerprint, StringComparison.Ordinal))
            {
                predicate = static _ => true;
                return true;
            }

            predicate = null;
            return false;
        }

        public bool TryGetProjection(
            Type type,
            string fingerprint,
            out Func<object, ProjectedEventField[]>? projectFields)
        {
            _ = type;
            _ = fingerprint;
            projectFields = null;
            return false;
        }
    }
}

using System.Reflection;
using SiftQL.Compiler;
using SiftQL.Expressions;
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
}

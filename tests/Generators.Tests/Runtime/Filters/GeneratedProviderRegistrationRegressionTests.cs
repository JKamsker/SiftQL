using System.Reflection;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Projected;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class GeneratedProviderRegistrationRegressionTests
{
    [Fact]
    public void SameAssemblyProviderRegistrationDoesNotDropEarlierSubjectSchemas()
    {
        (Type firstType, Type secondType) = EmitSubjectTypes();
        var filter = FilterExpression.Compare(
            "GeneratedFlag",
            FilterOperator.Equal,
            FilterValue.From(true));
        var index = new FilterSubscriptionIndex<string>(firstType);
        object subject = Activator.CreateInstance(firstType)!;

        GeneratedFilterSchemaRegistry.Register(firstType.Assembly, FirstProvider);
        index.Add("first", filter);
        Assert.Equal(["first"], index.SnapshotMatches(subject));

        GeneratedFilterSchemaRegistry.Register(firstType.Assembly, SecondProvider);

        Assert.Equal(["first"], index.SnapshotMatches(subject));

        bool FirstProvider(Type candidate, out FilterSchema? schema)
        {
            if (candidate != firstType)
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
                    TestFilterHelpers.ReservedField("subjectName", static subject => subject.GetType().Name),
                    BooleanField("GeneratedFlag", true),
                ]);
            return true;
        }

        bool SecondProvider(Type candidate, out FilterSchema? schema)
        {
            if (candidate != secondType)
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
                    TestFilterHelpers.ReservedField("subjectName", static subject => subject.GetType().Name),
                    BooleanField("OtherGeneratedFlag", true),
                ]);
            return true;
        }
    }

    private static FilterField BooleanField(string name, bool value) =>
        new(
            name,
            typeof(bool),
            FilterFieldKind.Scalar,
            _ => value,
            new FilterScalarAccessor(
                FilterScalarKind.Boolean,
                requiredBoolean: _ => value),
            ProjectionAccessor: _ => ProjectedEventValue.FromScalar(value));

    private static (Type First, Type Second) EmitSubjectTypes()
    {
        string assemblyName = "Plugin.GeneratedProviders." + Guid.NewGuid().ToString("N");
        SyntaxTree tree = CSharpSyntaxTree.ParseText("""
            using SiftQL;

            namespace Plugin.Events;

            public sealed class FirstEvent : IFilterSubject
            {
            }

            public sealed class SecondEvent : IFilterSubject
            {
            }
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, tree);
        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        AssertEx.True(emit.Success, "generated provider registration subjects emitted: " + string.Join(" | ", emit.Diagnostics));
        Assembly assembly = Assembly.Load(pe.ToArray());
        return (
            assembly.GetType("Plugin.Events.FirstEvent", throwOnError: true)!,
            assembly.GetType("Plugin.Events.SecondEvent", throwOnError: true)!);
    }
}

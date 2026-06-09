using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SiftQL.Expressions;
using Xunit;
using static SiftQL.Generators.Tests.QueryContextGeneratorTestSupport;

namespace SiftQL.Generators.Tests;

public sealed class QueryContextSourceGeneratorTests
{
    private const string OrderHint = "Sample.Contracts.IOrderQueryContext.SiftQueryContext.g.cs";

    [Fact]
    public void GeneratorEmitsDescriptorAndHelperExtensions()
    {
        QueryContextGeneratorRun run = RunGenerator(ParseTree(OrderContextSource(includeFactory: false)));
        string source = GeneratedSource(run, OrderHint);

        AssertEx.Contains("public const string ContextId = \"orders.server\";", source, "context id emitted");
        AssertEx.Contains("public const string CustomerMethodId = \"customer\";", source, "method id emitted");
        AssertEx.Contains("WithOrderQueryContext<TSubject>", source, "typed WithContext helper emitted");
        AssertEx.Contains("SiftQueryContextRegistry.Register(Descriptor);", source, "explicit registration emitted");
        AssertEx.Contains("SiftQueryContextDescriptor Descriptor", source, "descriptor emitted");
        AssertEx.Contains("EventProjectionContextIntrinsics.Method(", source, "manual include factory emitted");
        AssertNoCompilationErrors(run, "generated query context descriptor");

        Assembly assembly = EmitAssembly(run);
        Type helper = assembly.GetType(
            "Sample.Contracts.OrderQueryContextSiftQlExtensions",
            throwOnError: true)!;
        object descriptor = helper.GetProperty("Descriptor")!.GetValue(null)!;
        Assert.Equal("orders.server", descriptor.GetType().GetProperty("ContextId")!.GetValue(descriptor));
    }

    [Fact]
    public void GeneratedHelperRegistersDescriptorAndProducesQualifiedIncludes()
    {
        QueryContextGeneratorRun run = RunGenerator(ParseTree(OrderContextSource(includeFactory: true)));
        AssertNoCompilationErrors(run, "generated query context helper integration");
        Assembly assembly = EmitAssembly(run);
        Type factory = assembly.GetType("Sample.Contracts.QueryFactory", throwOnError: true)!;

        var pipeline = (EventPipelineExpression)factory.GetMethod("Build")!.Invoke(null, null)!;
        EventProjectionInclude include = pipeline.Stages
            .Where(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .SelectMany(static stage => stage.Projection.Includes)
            .Single();

        Assert.True(EventProjectionContextIntrinsics.TryParseMethod(
            include.Intrinsic,
            out string contextId,
            out string methodId,
            out string memberPath));
        Assert.Equal("orders.server", contextId);
        Assert.Equal("customer", methodId);
        Assert.Equal("IsActive", memberPath);
        Assert.Equal(EventProjectionArgumentKind.SourceField, include.Arguments.Single().Kind);
        Assert.Equal("CustomerId", include.Arguments.Single().SourcePath);
    }

    [Fact]
    public void GeneratorReportsDuplicateContextIds()
    {
        QueryContextGeneratorRun run = RunGenerator(ParseTree("""
            using SiftQL;
            namespace Sample.Contracts;

            [SiftQueryContext("orders.server")]
            public interface IFirstContext { Snapshot First(long id); }

            [SiftQueryContext("orders.server")]
            public interface ISecondContext { Snapshot Second(long id); }

            public sealed record Snapshot(bool Enabled);
            """));

        AssertHasDiagnostic(run, "SIFTQCTX002");
    }

    [Fact]
    public void GeneratorReportsDuplicateMethodIds()
    {
        QueryContextGeneratorRun run = RunGenerator(ParseTree("""
            using SiftQL;
            namespace Sample.Contracts;

            [SiftQueryContext("orders.server")]
            public interface IOrderQueryContext
            {
                [SiftQueryContextMethod("lookup")]
                Snapshot Customer(long id);

                [SiftQueryContextMethod("lookup")]
                Snapshot Account(long id);
            }

            public sealed record Snapshot(bool Enabled);
            """));

        AssertHasDiagnostic(run, "SIFTQCTX004");
    }

    [Fact]
    public void GeneratorReportsUnsupportedMethodShapes()
    {
        QueryContextGeneratorRun run = RunGenerator(ParseTree("""
            using SiftQL;
            namespace Sample.Contracts;

            [SiftQueryContext("orders.server")]
            public interface IOrderQueryContext
            {
                void Clear();
                Snapshot Generic<T>(T value);
                Snapshot Ref(ref long id);
            }

            public sealed record Snapshot(bool Enabled);
            """));

        AssertHasDiagnostic(run, "SIFTQCTX003");
    }

    [Fact]
    public void GeneratorEscapesKeywordMethodNameInDescriptor()
    {
        QueryContextGeneratorRun run = RunGenerator(ParseTree("""
            using SiftQL;
            namespace Sample.Contracts;

            [SiftQueryContext("keywords")]
            public interface IKeywordContext
            {
                int @class();
            }
            """));

        AssertNoCompilationErrors(run, "keyword query context method");
    }

    [Fact]
    public void GeneratorEmitsNonFiniteFloatingDefaultValues()
    {
        QueryContextGeneratorRun run = RunGenerator(ParseTree("""
            using SiftQL;
            namespace Sample.Contracts;

            [SiftQueryContext("defaults")]
            public interface IDefaultContext
            {
                Snapshot Lookup(double score = double.NaN, float ratio = float.PositiveInfinity);
            }

            public sealed record Snapshot(bool Enabled);
            """));

        AssertNoCompilationErrors(run, "non-finite query context default values");
    }

    [Fact]
    public void GeneratorReportsDuplicateIncludeFactorySignatures()
    {
        QueryContextGeneratorRun run = RunGenerator(ParseTree("""
            using SiftQL;
            namespace Sample.Contracts;

            [SiftQueryContext("overloads")]
            public interface IOverloadContext
            {
                [SiftQueryContextMethod("lookup-by-id")]
                Snapshot Lookup(long id);

                [SiftQueryContextMethod("lookup-by-name")]
                Snapshot Lookup(string name);
            }

            public sealed record Snapshot(bool Enabled);
            """));

        AssertHasDiagnostic(run, "SIFTQCTX003");
    }

    [Fact]
    public void GeneratorHandlesInternalQueryContextAccessibility()
    {
        QueryContextGeneratorRun run = RunGenerator(ParseTree("""
            using SiftQL;
            namespace Sample.Contracts;

            [SiftQueryContext("internal-context")]
            internal interface IInternalContext
            {
                Snapshot Lookup(long id);
            }

            internal sealed record Snapshot(bool Enabled);
            """));

        AssertNoCompilationErrors(run, "internal query context helper");
    }

    [Fact]
    public void GeneratorCachesContextForUnrelatedCompilationChange()
    {
        SyntaxTree contextTree = ParseTree(OrderContextSource(includeFactory: false));
        SyntaxTree unrelatedTree = ParseTree("namespace Sample.Contracts; internal static class Other { public const int Value = 1; }");
        CSharpCompilation compilation = CreateCompilation(contextTree, unrelatedTree);
        GeneratorDriver driver = CreateDriver(trackIncrementalSteps: true);
        driver = driver.RunGenerators(compilation);

        SyntaxTree changedUnrelatedTree = ParseTree("namespace Sample.Contracts; internal static class Other { /* trivia */ public const int Value = 1; }");
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(unrelatedTree, changedUnrelatedTree));

        var outputs = TrackedOutputs(driver.GetRunResult(), "QueryContextDiscovery");
        AssertEx.True(outputs.Length > 0, "QueryContextDiscovery produced tracked output");
        AssertEx.True(
            outputs.All(static output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
            "QueryContextDiscovery stayed cached for unrelated compilation change. Reasons: " +
                string.Join(", ", outputs.Select(static output => output.Reason)));
    }

    private static string OrderContextSource(bool includeFactory) =>
        $$"""
        using SiftQL;
        using SiftQL.Expressions;

        namespace Sample.Contracts;

        [SiftQueryContext("orders.server")]
        public interface IOrderQueryContext
        {
            [SiftQueryContextMethod("customer")]
            CustomerSnapshot Customer(long customerId);
        }

        public sealed record CustomerSnapshot(bool IsActive, string Tier);
        public sealed record OrderEvent(long OrderId, long CustomerId) : IFilterSubject;

        {{(includeFactory ? """
        public static class QueryFactory
        {
            public static EventPipelineExpression Build() =>
                QueryKernel.For<OrderEvent>()
                    .WithOrderQueryContext()
                    .Where(static (ev, ctx) => ctx.Customer(ev.CustomerId).IsActive)
                    .Select(static (ev, ctx) => new
                    {
                        Active = ctx.Customer(ev.CustomerId).IsActive,
                    })
                    .ToQueryKernel()
                    .Pipeline;
        }
        """ : string.Empty)}}
        """;
}

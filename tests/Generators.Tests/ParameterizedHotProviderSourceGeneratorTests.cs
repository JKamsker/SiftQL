using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

internal static class ParameterizedHotProviderSourceGeneratorTests
{
    public static void RunAll()
    {
        GeneratorEmitsParameterizedHotFilterAndProjectionProvider();
    }

    private static void GeneratorEmitsParameterizedHotFilterAndProjectionProvider()
    {
        const string assemblyName = "Plugin.Hot.Parameterized";
        FilterExpression manifestFilter = ItemIdFilter(7);
        FilterExpression runtimeFilter = ItemIdFilter(9);
        EventProjectionExpression manifestProjection = Projection(limit: 3);
        EventProjectionExpression runtimeProjection = Projection(limit: 5);
        string manifestJson = HotManifestJson(assemblyName, manifestFilter, manifestProjection);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("parameterized.siftql-hot.json", manifestJson),
            PluginEventTree());

        AssertEx.Equal(0, run.Diagnostics.Length, "parameterized generator diagnostics");
        string source = run.Result.Results[0].GeneratedSources
            .Single(item => item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal))
            .SourceText
            .ToString();
        AssertEx.Contains("TryGetParameterizedFilter", source, "parameterized filter lookup emitted");
        AssertEx.Contains("TryGetParameterizedProjection", source, "parameterized projection lookup emitted");
        AssertNoCompilationErrors(run, "parameterized hot provider");

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using LoadedHotProvider loaded = HotProviderTestLoader.Load(
            run.OutputCompilation,
            assemblyName,
            manifestJson,
            "parameterized hot provider assembly");
        Assembly assembly = loaded.Assembly;
        Type eventType = assembly.GetType("Plugin.Events.PluginOwnedEvent", throwOnError: true)!;
        object matching = Event(eventType, itemId: 9);
        object nonmatching = Event(eventType, itemId: 7);

        CompiledKernel kernel = FilterCompiler.Compile(eventType, runtimeFilter, FilterCompilerOptions.Tiered);
        AssertEx.True(!kernel.IsTiered, "parameterized hot filter provider beat tiered fallback");
        AssertEx.True(kernel.Matches(matching), "parameterized hot filter matched runtime value");
        AssertEx.True(!kernel.Matches(nonmatching), "parameterized hot filter did not bake manifest value");

        var compiledProjection = ProjectionCompiler.Compile<object>(
            eventType,
            runtimeProjection,
            EchoLimit,
            ProjectionCompilerOptions.Tiered);
        AssertEx.True(!compiledProjection.IsTiered, "parameterized hot projection provider beat tiered fallback");
        ProjectedEvent projected = compiledProjection
            .ProjectAsync(matching, new object(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        AssertEx.Equal(9L, projected.Fields.Single().Value.Integer, "parameterized hot projection field");
        AssertEx.Equal(5L, projected.Context.Single().Value.Integer, "runtime include kept bound parameter");
    }

    private static FilterExpression ItemIdFilter(long itemId) =>
        FilterExpression.Compare(
            "ItemId",
            FilterOperator.Equal,
            FilterValue.From(itemId) with { ParameterKey = "p0" });

    private static EventProjectionExpression Projection(long limit) =>
        EventProjectionExpression
            .Select("ItemId")
            .WithIncludes(
            [
                new EventProjectionInclude(
                    "test.limit",
                    "limit",
                    new EventProjectionArgument(
                        "limit",
                        FilterValue.From(limit) with { ParameterKey = "p0" })),
            ]);

    private static GeneratorRun RunGenerator(
        string assemblyName,
        AdditionalText hotManifest,
        params SyntaxTree[] extraTrees)
    {
        CSharpCompilation compilation = CreateCompilation(assemblyName, extraTrees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(hotManifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new GeneratorRun(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, params SyntaxTree[] extraTrees)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterExpression).Assembly.Location);
        AddReference(references, typeof(FilterSchema).Assembly.Location);

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: extraTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static AdditionalText HotManifest(
        string assemblyName,
        FilterExpression filter,
        EventProjectionExpression projection) =>
        new InMemoryAdditionalText(
            "parameterized.siftql-hot.json",
            HotManifestJson(assemblyName, filter, projection));

    private static string HotManifestJson(
        string assemblyName,
        FilterExpression filter,
        EventProjectionExpression projection)
    {
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                Entry("filter", assemblyName, Fingerprint(filter), JsonSerializer.SerializeToElement(filter)),
                Entry("projection", assemblyName, ProjectionFingerprint(projection), JsonSerializer.SerializeToElement(projection)),
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static HotCompilationManifestEntry Entry(
        string kind,
        string assemblyName,
        string fingerprint,
        JsonElement definition) =>
        new()
        {
            Key = $"{kind}|Plugin.Events.PluginOwnedEvent|{fingerprint}",
            Kind = kind,
            SubjectType = "Plugin.Events.PluginOwnedEvent, " + assemblyName,
            Fingerprint = fingerprint,
            Definition = definition,
        };

    private static string Fingerprint(FilterExpression expression) =>
        InvokeFingerprint("SiftQL.Compiler.FilterExpressionFingerprint", expression);

    private static string ProjectionFingerprint(EventProjectionExpression projection) =>
        InvokeFingerprint("SiftQL.Projection.ProjectionExpressionFingerprint", projection);

    private static string InvokeFingerprint(string typeName, object expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(typeName, throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private static CompiledProjection<object>.IncludeProjector EchoLimit(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        int limit = ProjectionIncludeArguments.RequiredInt(include, "limit");
        return new CompiledProjection<object>.IncludeProjector(
            include.ResultName,
            (_, _, _) => ValueTask.FromResult(ProjectedEventValue.FromScalar(limit)));
    }

    private static object Event(Type eventType, long itemId) =>
        Activator.CreateInstance(eventType, Guid.NewGuid(), 1L, itemId, 2)!;

    private static SyntaxTree PluginEventTree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record PluginOwnedEvent(
                Guid EventId,
                long CharacterId,
                long ItemId,
                int Quantity) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private static void AssertNoCompilationErrors(GeneratorRun run, string label)
    {
        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, label + " compilation errors: " + string.Join(" | ", errors.Take(8)));
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }

    private sealed record GeneratorRun(
        GeneratorDriverRunResult Result,
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

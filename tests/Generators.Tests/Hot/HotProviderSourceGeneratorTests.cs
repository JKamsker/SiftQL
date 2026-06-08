using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
// removed: source-specific value types
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

public sealed class HotProviderSourceGeneratorTests
{
    [Fact]
    public async Task GeneratorEmitsAndRegistersHotProvider()
    {
        const string assemblyName = "Plugin.Hot.Loaded";
        var filter = FilterExpression.Compare(
            "CharacterId",
            FilterOperator.Equal,
            FilterValue.From(7L));
        var projection = EventProjectionExpression.Select("CharacterId");
        string manifestJson = HotManifestJson(assemblyName, filter, projection);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("filters.siftql-hot.json", manifestJson),
            HotProviderPluginEventSource.Tree());

        AssertEx.Equal(0, run.Diagnostics.Length, "generator driver diagnostics");
        string source = run.Result.Results[0].GeneratedSources
            .Single(item => item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal))
            .SourceText
            .ToString();
        AssertEx.Contains("IPrecompiledTieredProvider", source, "hot provider contract emitted");
        AssertEx.Contains("[ModuleInitializer]", source, "hot provider auto-registration emitted");
        AssertEx.Contains("switch (fingerprint)", source, "hot provider lookup uses hashed string dispatch");
        AssertNoCompilationErrors(run, "hot provider");

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using LoadedHotProvider loaded = HotProviderTestLoader.Load(
            run.OutputCompilation,
            assemblyName,
            manifestJson,
            "hot provider assembly");
        Assembly assembly = loaded.Assembly;
        Type eventType = assembly.GetType("Plugin.Events.PluginOwnedEvent", throwOnError: true)!;
        object matching = Event(eventType, characterId: 7);
        object nonmatching = Event(eventType, characterId: 8);

        CompiledKernel kernel = FilterCompiler.Compile(eventType, filter, FilterCompilerOptions.Tiered);
        AssertEx.True(!kernel.IsTiered, "hot filter provider beat tiered fallback");
        AssertEx.True(kernel.Matches(matching), "hot filter matched");
        AssertEx.True(!kernel.Matches(nonmatching), "hot filter rejected");

        var compiledProjection = ProjectionCompiler.Compile<object>(
            eventType,
            projection,
            RejectInclude,
            ProjectionCompilerOptions.Tiered);
        AssertEx.True(!compiledProjection.IsTiered, "hot projection provider beat tiered fallback");
        ProjectedEvent projected = await compiledProjection.ProjectAsync(
            matching,
            new object(),
            CancellationToken.None);
        AssertEx.Equal(7L, projected.Fields.Single().Value.Integer, "hot projected value");
    }

    [Fact]
    public void GeneratorRejectsStaleGeneratorVersion()
    {
        string json = """
            {
              "Schema": "siftql.hot.v1",
              "RuntimeVersion": "10.0.0",
              "FilterEngineVersion": "tiered-v1",
              "GeneratorVersion": "old",
              "Entries": []
            }
            """;
        GeneratorRun run = RunGenerator("Plugin.Hot.Stale", new InMemoryAdditionalText("stale.siftql-hot.json", json));

        Diagnostic[] diagnostics = run.Diagnostics
            .Where(static item => item.Id == "FSFHOT004")
            .ToArray();
        AssertEx.Equal(1, diagnostics.Length, "stale generator version diagnostic count");
        AssertEx.Equal(0, run.Result.Results[0].GeneratedSources.Length, "stale manifest emitted no source");
    }

    [Fact]
    public void GeneratorResolvesClosedGenericSubjectFromAssemblyQualifiedManifest()
    {
        const string assemblyName = "Plugin.Hot.Generic";
        var filter = FilterExpression.Compare(
            "ItemId",
            FilterOperator.Equal,
            FilterValue.From(7L));
        string fingerprint = Fingerprint(filter);
        string subjectType = "Plugin.Events.GenericEvent`1[[" +
            typeof(int).AssemblyQualifiedName +
            "]], " +
            assemblyName;
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|" + subjectType + "|" + fingerprint,
                    Kind = "filter",
                    SubjectType = subjectType,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        };
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("generic.siftql-hot.json", JsonSerializer.Serialize(manifest)),
            CSharpSyntaxTree.ParseText("""
                using System;
                using SiftQL;

                namespace Plugin.Events;

                public sealed record GenericEvent<T>(
                    Guid EventId,
                    long ItemId) : IFilterSubject;
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));

        Diagnostic[] diagnostics = run.Diagnostics
            .Where(static item => item.Id == "FSFHOT009")
            .ToArray();

        AssertEx.Equal(0, diagnostics.Length, "closed generic subject resolved");
        AssertEx.Equal(
            1,
            run.Result.Results[0].GeneratedSources.Count(static item =>
                item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal)),
            "closed generic provider source emitted");
        AssertNoCompilationErrors(run, "closed generic hot provider");
    }

    [Fact]
    public void StartupLoaderValidatesAndLoadsGeneratedProvider()
    {
        string assemblyName = "Plugin.Hot.Loader";
        var filter = FilterExpression.Compare(
            "CharacterId",
            FilterOperator.Equal,
            FilterValue.From(9L));
        var projection = EventProjectionExpression.Select("CharacterId");
        string manifestJson = HotManifestJson(assemblyName, filter, projection);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("loader.siftql-hot.json", manifestJson),
            HotProviderPluginEventSource.Tree());

        string directory = Path.Combine(Path.GetTempPath(), "SiftQLHotProviderLoader", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, "Plugin.Hot.Loader.dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = run.OutputCompilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "loader hot provider assembly emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });
        AssertEx.True(result.Loaded, "startup loader loaded generated provider: " + result.Message);

        Type eventType = result.Assembly!.GetType("Plugin.Events.PluginOwnedEvent", throwOnError: true)!;
        CompiledKernel kernel = FilterCompiler.Compile(eventType, filter, FilterCompilerOptions.Tiered);
        AssertEx.True(!kernel.IsTiered, "startup-loaded provider beat tiered fallback");
        AssertEx.True(kernel.Matches(Event(eventType, characterId: 9)), "startup-loaded provider matched");

        File.WriteAllText(manifestPath, manifestJson.Replace("10.0.0", "10.0.1"));
        using HotTieredProviderLoadResult stale = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });
        AssertEx.Equal(
            HotTieredProviderLoadStatus.ManifestHashMismatch,
            stale.Status,
            "startup loader rejected stale manifest hash");
    }

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
        EventProjectionExpression projection)
    {
        return new InMemoryAdditionalText(
            "filters.siftql-hot.json",
            HotManifestJson(assemblyName, filter, projection));
    }

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
                new HotCompilationManifestEntry
                {
                    Key = "filter|Plugin.Events.PluginOwnedEvent|" + Fingerprint(filter),
                    Kind = "filter",
                    SubjectType = "Plugin.Events.PluginOwnedEvent, " + assemblyName,
                    Fingerprint = Fingerprint(filter),
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
                new HotCompilationManifestEntry
                {
                    Key = "projection|Plugin.Events.PluginOwnedEvent|" + ProjectionFingerprint(projection),
                    Kind = "projection",
                    SubjectType = "Plugin.Events.PluginOwnedEvent, " + assemblyName,
                    Fingerprint = ProjectionFingerprint(projection),
                    Definition = JsonSerializer.SerializeToElement(projection),
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private static string ProjectionFingerprint(EventProjectionExpression projection) =>
        InvokeFingerprint(
            "SiftQL.Projection.ProjectionExpressionFingerprint",
            projection);

    private static string InvokeFingerprint(string typeName, object expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(typeName, throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private static object Event(Type eventType, long characterId)
    {
        Type skillRef = eventType.Assembly.GetType("Plugin.Events.SkillRef", throwOnError: true)!;
        Type eventKind = eventType.Assembly.GetType("Plugin.Events.PluginEventKind", throwOnError: true)!;
        return Activator.CreateInstance(
            eventType,
            Guid.NewGuid(),
            characterId,
            Activator.CreateInstance(skillRef, 10, 1),
            Enum.ToObject(eventKind, 1),
            new[] { 1, 2 })!;
    }

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

    private sealed record GeneratorRun(GeneratorDriverRunResult Result, Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics);
}

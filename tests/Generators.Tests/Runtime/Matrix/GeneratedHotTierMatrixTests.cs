using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratedHotTierMatrixTests
{
    [Fact]
    public async Task GeneratedHotDefaultProjectionIncludesScalarArrays()
    {
        const string assemblyName = "Plugin.Hot.MatrixDefault";
        EventProjectionExpression projection = EventProjectionExpression.Default;
        string manifest = ManifestJson(
            ProjectionEntry(PluginSubject(assemblyName), projection));

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using LoadedHotProvider loaded = LoadProvider(assemblyName, manifest);
        Type eventType = loaded.Assembly.GetType("Plugin.Events.PluginOwnedEvent", throwOnError: true)!;

        CompiledProjection<object> compiled = ProjectionCompiler.Compile<object>(
            eventType,
            projection,
            RejectInclude,
            ProjectionCompilerOptions.Tiered);
        ProjectedEvent projected = await compiled.ProjectAsync(
            Event(eventType, characterId: 7, tokens: [100, 200]),
            new object(),
            CancellationToken.None);

        Assert.False(compiled.IsTiered);
        Assert.True(projected.TryGetField("Tokens", out var tokens));
        Assert.Equal(ProjectedEventValueKind.Array, tokens.Kind);
        Assert.Equal([100L, 200L], tokens.Values.Select(static item => item.Integer).ToArray());
    }

    [Fact]
    public async Task GeneratedHotPipelineRunsEveryStage()
    {
        const string assemblyName = "Plugin.Hot.MatrixPipeline";
        FilterExpression sourceFilter = FilterExpression.Compare(
            "CharacterId",
            FilterOperator.GreaterThanOrEqual,
            FilterValue.From(7L));
        EventProjectionExpression firstProjection = EventProjectionExpression.Select(
            "CharacterId",
            "Tokens");
        FilterExpression projectedFilter = FilterExpression.Contains(
            ProjectedEventPaths.Field("Tokens"),
            FilterValue.From(2L));
        EventProjectionExpression finalProjection = EventProjectionExpression.Default.WithFields(
            [new EventProjectionField(ProjectedEventPaths.Field("CharacterId"), "CharacterId")]);
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendSourceFilter(sourceFilter)
            .AppendProjection(firstProjection)
            .AppendFilter(projectedFilter)
            .AppendProjection(finalProjection);
        string manifest = ManifestJson(
            FilterEntry(PluginSubject(assemblyName), sourceFilter),
            ProjectionEntry(PluginSubject(assemblyName), firstProjection),
            FilterEntry(typeof(ProjectedEvent).AssemblyQualifiedName!, projectedFilter),
            ProjectionEntry(typeof(ProjectedEvent).AssemblyQualifiedName!, finalProjection));

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using LoadedHotProvider loaded = LoadProvider(assemblyName, manifest);
        Type eventType = loaded.Assembly.GetType("Plugin.Events.PluginOwnedEvent", throwOnError: true)!;
        CompiledEventPipeline<object> compiled = EventPipelineCompiler.Compile<object>(
            eventType,
            pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Tiered);

        ProjectedEvent? rejectedSource = await compiled.ProjectAsync(
            Event(eventType, characterId: 6, tokens: [2]),
            new object(),
            CancellationToken.None);
        ProjectedEvent? rejectedProjected = await compiled.ProjectAsync(
            Event(eventType, characterId: 7, tokens: [3]),
            new object(),
            CancellationToken.None);
        ProjectedEvent? accepted = await compiled.ProjectAsync(
            Event(eventType, characterId: 7, tokens: [2]),
            new object(),
            CancellationToken.None);

        Assert.Null(rejectedSource);
        Assert.Null(rejectedProjected);
        Assert.NotNull(accepted);
        Assert.Equal(7, accepted!.Field("CharacterId").Integer);
    }

    private static LoadedHotProvider LoadProvider(string assemblyName, string manifest)
    {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = RunGenerator(
            assemblyName,
            manifest);
        Assert.Empty(diagnostics);
        AssertNoCompilationErrors(output);
        return HotProviderTestLoader.Load(output, assemblyName, manifest, "generated hot matrix");
    }

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        string assemblyName,
        string manifest)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(
            assemblyName,
            HotProviderPluginEventSource.Tree());
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("matrix.siftql-hot.json", manifest)),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return (outputCompilation, diagnostics);
    }

    private static string ManifestJson(params HotCompilationManifestEntry[] entries) =>
        JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries = entries,
        });

    private static HotCompilationManifestEntry FilterEntry(
        string subjectType,
        FilterExpression expression) =>
        Entry(
            "filter",
            subjectType,
            FilterExpressionFingerprint.Create(expression),
            JsonSerializer.SerializeToElement(expression));

    private static HotCompilationManifestEntry ProjectionEntry(
        string subjectType,
        EventProjectionExpression projection) =>
        Entry(
            "projection",
            subjectType,
            ProjectionExpressionFingerprint.Create(projection),
            JsonSerializer.SerializeToElement(projection));

    private static HotCompilationManifestEntry Entry(
        string kind,
        string subjectType,
        string fingerprint,
        JsonElement definition) =>
        new()
        {
            Key = kind + "|" + subjectType + "|" + fingerprint,
            Kind = kind,
            SubjectType = subjectType,
            Fingerprint = fingerprint,
            Definition = definition,
        };

    private static string PluginSubject(string assemblyName) =>
        "Plugin.Events.PluginOwnedEvent, " + assemblyName;

    private static object Event(Type eventType, long characterId, int[] tokens)
    {
        Type skillRef = eventType.Assembly.GetType("Plugin.Events.SkillRef", throwOnError: true)!;
        Type eventKind = eventType.Assembly.GetType("Plugin.Events.PluginEventKind", throwOnError: true)!;
        return Activator.CreateInstance(
            eventType,
            Guid.NewGuid(),
            characterId,
            Activator.CreateInstance(skillRef, 10, 1),
            Enum.ToObject(eventKind, 1),
            tokens)!;
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private static void AssertNoCompilationErrors(Compilation output)
    {
        Diagnostic[] errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(errors);
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }
}

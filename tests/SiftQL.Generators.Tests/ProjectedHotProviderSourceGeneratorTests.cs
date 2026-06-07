using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
// removed: game-specific events
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

internal static class ProjectedHotProviderSourceGeneratorTests
{
    public static void RunAll()
    {
        GeneratorSupportsProjectedEventDynamicFields();
        GeneratorDefaultProjectedEventProjectionMatchesRuntime();
    }

    private static void GeneratorSupportsProjectedEventDynamicFields()
    {
        var filter = FilterExpression.Compare(
            ProjectedEventPaths.Field(nameof(ItemUsedEvent.ItemId)),
            FilterOperator.Equal,
            FilterValue.From(100L));
        var projection = EventProjectionExpression.Default.WithFields(
            [new EventProjectionField(ProjectedEventPaths.Field(nameof(ItemUsedEvent.ItemId)), "SlimItem")]);
        GeneratorRun run = RunGenerator(HotManifest("Projected.Hot", filter, projection));

        AssertEx.Equal(0, run.Diagnostics.Length, "generator driver diagnostics");
        string source = run.Result.Results[0].GeneratedSources
            .Single(item => item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal))
            .SourceText
            .ToString();
        AssertEx.Contains("ProjectedEventFilterSchema.CreateField", source, "projected field lookup emitted");
        AssertNoCompilationErrors(run, "projected hot provider");

        using var pe = new MemoryStream();
        EmitResult emit = run.OutputCompilation.Emit(pe);
        AssertEx.True(emit.Success, "projected hot provider emitted: " + string.Join(" | ", emit.Diagnostics));
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        Assembly.Load(pe.ToArray());

        var pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Select(
                nameof(ItemUsedEvent.ItemId),
                nameof(ItemUsedEvent.Quantity)))
            .AppendFilter(filter)
            .AppendProjection(projection);
        var compiled = EventPipelineCompiler.Compile<object>(
            typeof(ItemUsedEvent),
            pipeline,
            RejectInclude,
            EventPipelineCompilerOptions.Tiered);

        ProjectedEvent? projected = compiled.ProjectAsync(
                new ItemUsedEvent(Guid.NewGuid(), CharacterId: 1, ItemId: 100, Quantity: 2),
                new object(),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AssertEx.True(projected is not null, "projected hot pipeline matched");
        ProjectedEventField field = projected!.Fields.Single();
        AssertEx.Equal("SlimItem", field.Name, "projected hot field name");
        AssertEx.Equal(100L, field.Value.Integer, "projected hot field value");
    }

    private static void GeneratorDefaultProjectedEventProjectionMatchesRuntime()
    {
        EventProjectionExpression projection = EventProjectionExpression.Default;
        GeneratorRun run = RunGenerator(HotManifest("Projected.Hot", FilterExpression.Any, projection));

        AssertEx.Equal(0, run.Diagnostics.Length, "default projected generator diagnostics");
        string source = run.Result.Results[0].GeneratedSources
            .Single(item => item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal))
            .SourceText
            .ToString();
        AssertEx.Contains("TryGetProjection", source, "default projected hot projection emitted");
        AssertNoCompilationErrors(run, "default projected hot provider");

        using var pe = new MemoryStream();
        EmitResult emit = run.OutputCompilation.Emit(pe);
        AssertEx.True(emit.Success, "default projected hot provider emitted: " + string.Join(" | ", emit.Diagnostics));

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        Assembly assembly = Assembly.Load(pe.ToArray());
        IPrecompiledTieredProvider provider = Provider(assembly);
        AssertEx.True(
            provider.TryGetProjection(typeof(ProjectedEvent), ProjectionFingerprint(projection), out var projectFields) &&
            projectFields is not null,
            "default projected hot provider exposes projection");
        using var registration = PrecompiledTieredProviderRegistry.Register(provider);

        var compiled = ProjectionCompiler.Compile<object>(
            typeof(ProjectedEvent),
            projection,
            RejectInclude,
            ProjectionCompilerOptions.Tiered);
        AssertEx.True(!compiled.IsTiered, "default projected hot provider beat tiered fallback");

        ProjectedEvent projected = compiled.ProjectAsync(
                new ProjectedEvent
                {
                    EventType = typeof(ItemUsedEvent).FullName!,
                    EventName = nameof(ItemUsedEvent),
                },
                new object(),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AssertEx.Equal(0, projected.Fields.Length, "default projected hot fields match runtime");
    }

    private static GeneratorRun RunGenerator(AdditionalText manifest)
    {
        CSharpCompilation compilation = CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(manifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new GeneratorRun(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation()
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(ProjectedEvent).Assembly.Location);
        AddReference(references, typeof(FilterSchema).Assembly.Location);
        return CSharpCompilation.Create(
            "Projected.Hot",
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static AdditionalText HotManifest(
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
        return new InMemoryAdditionalText("projected.fourstory-hot.json", JsonSerializer.Serialize(manifest));
    }

    private static HotCompilationManifestEntry Entry(
        string kind,
        string assemblyName,
        string fingerprint,
        JsonElement definition) =>
        new()
        {
            Key = kind + "|" + typeof(ProjectedEvent).FullName + "|" + fingerprint,
            Kind = kind,
            SubjectType = typeof(ProjectedEvent).FullName + ", " + assemblyName,
            Fingerprint = fingerprint,
            Definition = definition,
        };

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private static string Fingerprint(FilterExpression expression) =>
        InvokeFingerprint("SiftQL.FilterExpressionFingerprint", expression);

    private static string ProjectionFingerprint(EventProjectionExpression projection) =>
        InvokeFingerprint("SiftQL.Projection.ProjectionExpressionFingerprint", projection);

    private static IPrecompiledTieredProvider Provider(Assembly assembly)
    {
        Type providerType = assembly.GetTypes()
            .Single(type => typeof(IPrecompiledTieredProvider).IsAssignableFrom(type));
        return (IPrecompiledTieredProvider)Activator.CreateInstance(providerType, nonPublic: true)!;
    }

    private static string InvokeFingerprint(string typeName, object expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(typeName, throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
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

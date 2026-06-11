using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Projected;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class ProjectedHotProviderTimestampRegressionTests
{
    [Fact]
    public void GeneratorSupportsProjectedEventTimestampOrdering()
    {
        DateTimeOffset cutoff = new(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
        FilterExpression filter = FilterExpression.Compare(
            ProjectedEventPaths.Field("Instant"),
            FilterOperator.GreaterThan,
            FilterValue.From(cutoff));
        GeneratorRun run = RunGenerator(HotManifestJson(filter));

        AssertEx.Equal(0, run.Diagnostics.Length, "projected timestamp hot diagnostics");
        AssertNoCompilationErrors(run, "projected timestamp hot provider");
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var pe = new MemoryStream();
        EmitResult emit = run.OutputCompilation.Emit(pe);
        AssertEx.True(emit.Success, "projected timestamp hot provider emitted: " + string.Join(" | ", emit.Diagnostics));
        Assembly assembly = Assembly.Load(pe.ToArray());
        IPrecompiledTieredProvider provider = Provider(assembly);

        AssertEx.True(
            provider.TryGetFilter(typeof(ProjectedEvent), FilterExpressionFingerprint.Create(filter), out var predicate) &&
            predicate is not null,
            "projected timestamp hot filter exposed");
        Assert.True(predicate!(Projected(cutoff.AddMinutes(1))));
        Assert.False(predicate(Projected(cutoff.AddMinutes(-1))));
    }

    private static ProjectedEvent Projected(DateTimeOffset instant) =>
        new()
        {
            EventType = typeof(ProjectedEvent).FullName!,
            EventName = nameof(ProjectedEvent),
            Fields =
            [
                new ProjectedEventField("Instant", ProjectedEventValue.FromScalar(instant)),
            ],
        };

    private static GeneratorRun RunGenerator(string manifestJson)
    {
        CSharpCompilation compilation = CreateCompilation();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("projected-timestamp.siftql-hot.json", manifestJson)),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation()
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(ProjectedEvent).Assembly.Location);
        AddReference(references, typeof(IPrecompiledTieredProvider).Assembly.Location);
        AddReference(references, typeof(FilterSchemaSourceGenerator).Assembly.Location);
        return CSharpCompilation.Create(
            "Projected.Hot.Timestamp",
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string HotManifestJson(FilterExpression filter)
    {
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|" + typeof(ProjectedEvent).FullName + "|" + fingerprint,
                    Kind = "filter",
                    SubjectType = typeof(ProjectedEvent).FullName + ", Projected.Hot.Timestamp",
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static IPrecompiledTieredProvider Provider(Assembly assembly)
    {
        Type providerType = assembly.GetTypes()
            .Single(type => typeof(IPrecompiledTieredProvider).IsAssignableFrom(type));
        return (IPrecompiledTieredProvider)Activator.CreateInstance(providerType, nonPublic: true)!;
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
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed record GeneratorRun(
        GeneratorDriverRunResult Result,
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

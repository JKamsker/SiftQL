using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Kernel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderInactiveParameterPayloadRegressionTests
{
    [Fact]
    public void InactivePayloadParametersKeepGeneratedHotProviderNonParameterized()
    {
        const string assemblyName = "Plugin.Hot.InactiveFilterParameter";
        FilterExpression cleanFilter = FilterExpression.Exists("CharacterId");
        FilterExpression manifestFilter = cleanFilter with
        {
            Value = FilterValue.From(7L) with { ParameterKey = "p0" },
        };
        string manifestJson = ManifestJson(assemblyName, manifestFilter);
        GeneratorRun run = RunGenerator(
            assemblyName,
            new InMemoryAdditionalText("inactive.siftql-hot.json", manifestJson),
            HotProviderPluginEventSource.Tree());

        AssertNoCompilationErrors(run.OutputCompilation);

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using LoadedHotProvider loaded = HotProviderTestLoader.Load(
            run.OutputCompilation,
            assemblyName,
            manifestJson,
            "inactive filter parameter hot provider");
        Type eventType = loaded.Assembly.GetType("Plugin.Events.PluginOwnedEvent", throwOnError: true)!;
        CompiledKernel kernel = FilterCompiler.Compile(
            eventType,
            cleanFilter,
            FilterCompilerOptions.Tiered);

        Assert.False(kernel.IsTiered);
    }

    private static string ManifestJson(string assemblyName, FilterExpression filter)
    {
        string fingerprint = TestFilterHelpers.Fingerprint(filter);
        return JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|Plugin.Events.PluginOwnedEvent|" + fingerprint,
                    Kind = "filter",
                    SubjectType = "Plugin.Events.PluginOwnedEvent, " + assemblyName,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        });
    }

    private static GeneratorRun RunGenerator(
        string assemblyName,
        AdditionalText hotManifest,
        params SyntaxTree[] extraTrees)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, extraTrees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create(hotManifest),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out _);
        return new(outputCompilation);
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

    private sealed record GeneratorRun(Compilation OutputCompilation);
}

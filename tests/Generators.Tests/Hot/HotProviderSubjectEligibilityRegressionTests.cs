using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderSubjectEligibilityRegressionTests
{
    [Fact]
    public void RejectsNestedPlainSubjectFilterThatRuntimeSchemaCannotMatch()
    {
        var filter = FilterExpression.Compare(
            "Location.MapId",
            FilterOperator.Equal,
            FilterValue.From(42L));
        GeneratorRun run = RunGenerator(Manifest(filter));

        Assert.Contains(run.Diagnostics, static diagnostic => diagnostic.Id == "FSFHOT009");
        Assert.Equal(0, HotProviderSourceCount(run));
    }

    private static string Manifest(FilterExpression filter)
    {
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|Plugin.Events.PlainEvent|" + fingerprint,
                    Kind = "filter",
                    SubjectType = "Plugin.Events.PlainEvent, Plugin.Hot.SubjectEligibility",
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static GeneratorRun RunGenerator(string manifestJson)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(
            "Plugin.Hot.SubjectEligibility",
            CSharpSyntaxTree.ParseText("""
                using System;

                namespace Plugin.Events;

                public sealed record Location(long MapId);

                public sealed record PlainEvent(Guid EventId, Location Location);
                """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("subject-eligibility.siftql-hot.json", manifestJson)),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static int HotProviderSourceCount(GeneratorRun run) =>
        run.Result.Results[0].GeneratedSources.Count(static item =>
            item.HintName.StartsWith("GeneratedHotTieredProvider_", StringComparison.Ordinal));

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

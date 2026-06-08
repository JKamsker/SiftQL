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
        GeneratorRun run = RunGenerator(
            Manifest("Plugin.Events.PlainEvent, Plugin.Hot.SubjectEligibility", filter),
            """
            using System;

            namespace Plugin.Events;

            public sealed record Location(long MapId);

            public sealed record PlainEvent(Guid EventId, Location Location);
            """);

        Assert.Contains(run.Diagnostics, static diagnostic => diagnostic.Id == "FSFHOT009");
        Assert.Equal(0, HotProviderSourceCount(run));
    }

    [Fact]
    public void RejectsClosedGenericNestedRecordFieldThatRuntimeSchemaCannotMatch()
    {
        var filter = FilterExpression.Compare(
            "Location.MapId",
            FilterOperator.Equal,
            FilterValue.From(42L));
        string subjectType = "Plugin.Events.GenericEvent`1[[" +
            typeof(int).AssemblyQualifiedName +
            "]], Plugin.Hot.SubjectEligibility";

        GeneratorRun run = RunGenerator(
            Manifest(subjectType, filter),
            """
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record Location(long MapId);

            public sealed record GenericEvent<T>(
                Guid EventId,
                Location Location) : IFilterSubject;
            """);

        Assert.Contains(run.Diagnostics, static diagnostic => diagnostic.Id == "FSFHOT009");
        Assert.Equal(0, HotProviderSourceCount(run));
    }

    [Fact]
    public void RejectsNestedFieldWhenContainingTypeBlocksGeneratedSchema()
    {
        var filter = FilterExpression.Compare(
            "Location.MapId",
            FilterOperator.Equal,
            FilterValue.From(42L));
        GeneratorRun run = RunGenerator(
            Manifest("Plugin.Events.Container+MovedEvent, Plugin.Hot.SubjectEligibility", filter),
            """
            using System;
            using SiftQL;

            namespace Plugin.Events;

            internal static class Container
            {
                public sealed record Location(long MapId);

                public sealed record MovedEvent(Guid EventId, Location Location) : IFilterSubject;
            }
            """);

        Assert.Contains(run.Diagnostics, static diagnostic => diagnostic.Id == "FSFHOT009");
        Assert.Equal(0, HotProviderSourceCount(run));
    }

    [Fact]
    public void RejectsTopLevelObjectFieldWhenRuntimeSchemaCannotMatch()
    {
        var filter = FilterExpression.Exists("Location");
        string subjectType = "Plugin.Events.GenericEvent`1[[" +
            typeof(int).AssemblyQualifiedName +
            "]], Plugin.Hot.SubjectEligibility";

        GeneratorRun run = RunGenerator(
            Manifest(subjectType, filter),
            """
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record Location(long MapId);

            public sealed record GenericEvent<T>(
                Guid EventId,
                Location Location) : IFilterSubject;
            """);

        Assert.Contains(run.Diagnostics, static diagnostic => diagnostic.Id == "FSFHOT009");
        Assert.Equal(0, HotProviderSourceCount(run));
    }

    [Fact]
    public void RejectsReservedMetadataPropertyCollisions()
    {
        var filter = FilterExpression.Compare(
            "ItemId",
            FilterOperator.Equal,
            FilterValue.From(7L));

        GeneratorRun run = RunGenerator(
            Manifest("Plugin.Events.CollisionEvent, Plugin.Hot.SubjectEligibility", filter),
            """
            using SiftQL;

            namespace Plugin.Events;

            public sealed record CollisionEvent(
                string SubjectType,
                long ItemId) : IFilterSubject;
            """);

        Assert.Contains(run.Diagnostics, static diagnostic => diagnostic.Id == "FSFHOT009");
        Assert.Equal(0, HotProviderSourceCount(run));
    }

    private static string Manifest(string subjectType, FilterExpression filter)
    {
        string fingerprint = FilterExpressionFingerprint.Create(filter);
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
        return JsonSerializer.Serialize(manifest);
    }

    private static GeneratorRun RunGenerator(string manifestJson, string source)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(
            "Plugin.Hot.SubjectEligibility",
            CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));
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

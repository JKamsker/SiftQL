using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Compiler;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projection;
using SiftQL.Values;
using SiftQL.Generators.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

internal static class HotProviderDuplicateFingerprintTests
{
    public static void RunAll()
    {
        DuplicateFingerprintEntriesDispatchBySubjectType();
        IdenticalEntriesEmitSingleSubjectBranch();
    }

    private static void DuplicateFingerprintEntriesDispatchBySubjectType()
    {
        const string assemblyName = "Plugin.Hot.DuplicateFingerprint";
        FilterExpression filter = FilterExpression.Compare(
            "CharacterId",
            FilterOperator.Equal,
            FilterValue.From(7L));
        string fingerprint = Fingerprint(filter);
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                Entry("Plugin.Events.FirstEvent", assemblyName, fingerprint, filter),
                Entry("Plugin.Events.SecondEvent", assemblyName, fingerprint, filter),
            ],
        };
        string manifestJson = JsonSerializer.Serialize(manifest);
        GeneratorRun run = RunGenerator(
            assemblyName,
            manifestJson,
            EventTree());

        AssertEx.Equal(0, run.Diagnostics.Length, "duplicate fingerprint diagnostics");
        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using LoadedHotProvider loaded = HotProviderTestLoader.Load(
            run.OutputCompilation,
            assemblyName,
            manifestJson,
            "duplicate fingerprint provider");
        Assembly assembly = loaded.Assembly;
        Type first = assembly.GetType("Plugin.Events.FirstEvent", throwOnError: true)!;
        Type second = assembly.GetType("Plugin.Events.SecondEvent", throwOnError: true)!;
        object firstMatching = Event(first, characterId: 7);
        object secondMatching = Event(second, characterId: 7);

        CompiledKernel firstKernel = FilterCompiler.Compile(first, filter, FilterCompilerOptions.Tiered);
        CompiledKernel secondKernel = FilterCompiler.Compile(second, filter, FilterCompilerOptions.Tiered);

        AssertEx.True(!firstKernel.IsTiered, "first duplicate fingerprint entry was hot");
        AssertEx.True(!secondKernel.IsTiered, "second duplicate fingerprint entry was hot");
        AssertEx.True(firstKernel.Matches(firstMatching), "first event matched");
        AssertEx.True(secondKernel.Matches(secondMatching), "second event matched");
    }

    private static void IdenticalEntriesEmitSingleSubjectBranch()
    {
        const string assemblyName = "Plugin.Hot.DuplicateIdentical";
        FilterExpression filter = FilterExpression.Compare(
            "CharacterId",
            FilterOperator.Equal,
            FilterValue.From(7L));
        string fingerprint = Fingerprint(filter);
        var manifest = new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                Entry("Plugin.Events.FirstEvent", assemblyName, fingerprint, filter),
                Entry("Plugin.Events.FirstEvent", assemblyName, fingerprint, filter),
            ],
        };
        GeneratorRun run = RunGenerator(
            assemblyName,
            JsonSerializer.Serialize(manifest),
            EventTree());

        string source = run.OutputCompilation.SyntaxTrees
            .Select(static tree => tree.ToString())
            .Single(static text => text.Contains("IPrecompiledTieredProvider", StringComparison.Ordinal));

        AssertEx.Equal(0, run.Diagnostics.Length, "identical duplicate diagnostics");
        AssertEx.Equal(
            1,
            Occurrences(source, "private static bool Filter_"),
            "identical duplicate emitted filter method count");
    }

    private static HotCompilationManifestEntry Entry(
        string typeName,
        string assemblyName,
        string fingerprint,
        FilterExpression filter) =>
        new()
        {
            Key = "filter|" + typeName + "|" + fingerprint,
            Kind = "filter",
            SubjectType = typeName + ", " + assemblyName,
            Fingerprint = fingerprint,
            Definition = JsonSerializer.SerializeToElement(filter),
        };

    private static object Event(Type eventType, long characterId) =>
        Activator.CreateInstance(eventType, Guid.NewGuid(), characterId)!;

    private static string Fingerprint(FilterExpression expression)
    {
        Type type = typeof(FilterCompiler).Assembly.GetType(
            "SiftQL.Compiler.FilterExpressionFingerprint",
            throwOnError: true)!;
        return (string)type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [expression])!;
    }

    private static SyntaxTree EventTree() =>
        CSharpSyntaxTree.ParseText("""
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record FirstEvent(Guid EventId, long CharacterId) : IFilterSubject;
            public sealed record SecondEvent(Guid EventId, long CharacterId) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static int Occurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static GeneratorRun RunGenerator(
        string assemblyName,
        string manifestJson,
        params SyntaxTree[] extraTrees)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, extraTrees);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("duplicate.siftql-hot.json", manifestJson)),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return new(outputCompilation, diagnostics);
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed record GeneratorRun(
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

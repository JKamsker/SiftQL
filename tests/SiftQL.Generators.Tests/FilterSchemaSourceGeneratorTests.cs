using System.Collections.Immutable;
using System.Reflection;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SiftQL.Generators.Tests;

internal static class FilterSchemaSourceGeneratorTests
{
    private const string RuntimeAssemblyName = "SiftQL";
    private const string BuiltInProviderHint = "GeneratedFilterSchemaProvider.g.cs";
    private const string CurrentProviderHint = "GeneratedCurrentFilterSchemaProvider.g.cs";

    public static void RunAll()
    {
        GeneratorEmitsServerAndClientSchemas();
        GeneratorEmitsPluginOwnedCurrentSchemas();
        GeneratedPluginOwnedProviderRegistersWithRuntime();
        GeneratorSkipsPluginSchemasWithoutFilterRuntime();
        GeneratorEmitsEmptyProviderWithoutAbstractionsReference();
        GeneratorCachesDiscoveryForUnrelatedCompilationChange();
        GeneratorCachesCurrentSchemaForUnrelatedCompilationChange();
    }

    private static void GeneratorEmitsServerAndClientSchemas()
    {
        GeneratorRun run = RunGenerator(
            RuntimeAssemblyName,
            includeAbstractions: true,
            includeFilters: true,
            includeFilterRuntimeReference: false);
        AssertEx.Equal(0, run.Diagnostics.Length, "generator driver diagnostics");
        AssertEx.Equal(1, run.Result.Results[0].GeneratedSources.Length, "generated source count");

        string source = GeneratedSource(run, BuiltInProviderHint);
        AssertEx.Contains("typeof(global::SiftQL.DamageDealtEvent)", source, "server event schema emitted");
        AssertEx.Contains("\"Attacker.ObjectId\"", source, "nested server value object field emitted");
        AssertEx.Contains("new FilterScalarAccessor(FilterScalarKind.Number", source, "typed number accessor emitted");
        AssertEx.Contains("ProjectionValueFactory.FromInt32", source, "typed projection accessor emitted");
        AssertEx.Contains("typeof(global::SiftQL.Input.ClientMouseEvent)", source, "client input schema emitted");
        AssertEx.Contains("\"OverPluginHud\"", source, "client event scalar field emitted");
        AssertEx.Contains("\"WindowId\"", source, "inherited UI event field emitted");
        AssertEx.Contains("typeof(global::SiftQL.Dtos.Character.PlayerSnapshot)", source, "client snapshot schema emitted");

        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, "generated provider compilation errors: " + string.Join(" | ", errors.Take(8)));
    }

    private static void GeneratorEmitsPluginOwnedCurrentSchemas()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.Tests",
            includeAbstractions: true,
            includeFilters: false,
            includeFilterRuntimeReference: true,
            PluginEventTree());

        string source = GeneratedSource(run, CurrentProviderHint);
        AssertEx.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", source, "provider registration emitted");
        AssertEx.Contains("GeneratedFilterSchemaRegistry.Register", source, "runtime registration emitted");
        AssertEx.Contains("typeof(global::Plugin.Events.PluginOwnedEvent)", source, "plugin event schema emitted");
        AssertEx.Contains("\"Skill.SkillId\"", source, "plugin value object field emitted");
        AssertEx.Contains("FilterArrayContains.ContainsInt32", source, "plugin array accessor emitted");
        AssertNoCompilationErrors(run, "plugin generated provider");
    }

    private static void GeneratedPluginOwnedProviderRegistersWithRuntime()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.Loaded",
            includeAbstractions: true,
            includeFilters: false,
            includeFilterRuntimeReference: true,
            PluginEventTree());

        using var pe = new MemoryStream();
        EmitResult emit = run.OutputCompilation.Emit(pe);
        AssertEx.True(emit.Success, "generated plugin assembly emitted: " + string.Join(" | ", emit.Diagnostics));

        Assembly assembly = Assembly.Load(pe.ToArray());
        Type eventType = assembly.GetType("Plugin.Events.PluginOwnedEvent", throwOnError: true)!;
        FilterSchema schema = FilterSchema.For(eventType);

        AssertEx.True(schema.TryGetField("CharacterId", out _), "generated scalar field registered");
        AssertEx.True(schema.TryGetField("Skill.SkillId", out _), "generated nested value object field registered");
        AssertEx.True(schema.TryGetField("Tokens", out var tokens) && tokens.ArrayAccessor is not null, "generated array field registered");
    }

    private static void GeneratorSkipsPluginSchemasWithoutFilterRuntime()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.NoRuntime",
            includeAbstractions: true,
            includeFilters: false,
            includeFilterRuntimeReference: false,
            PluginEventTree());

        AssertEx.Equal(0, run.Result.Results[0].GeneratedSources.Length, "no provider emitted without filter runtime");
        AssertNoCompilationErrors(run, "plugin event without filter runtime");
    }

    private static void GeneratorEmitsEmptyProviderWithoutAbstractionsReference()
    {
        GeneratorRun run = RunGenerator(
            RuntimeAssemblyName,
            includeAbstractions: false,
            includeFilters: true,
            includeFilterRuntimeReference: false);
        string source = GeneratedSource(run, BuiltInProviderHint);

        AssertEx.Contains("public static bool TryCreate(Type subjectType, out FilterSchema? schema)", source, "provider shape emitted");
        AssertEx.Contains("return false;", source, "empty provider rejects every type");
        AssertEx.DoesNotContain("SiftQL.Abstractions", source, "no abstraction type references emitted");
    }

    private static void GeneratorCachesDiscoveryForUnrelatedCompilationChange()
    {
        SyntaxTree unrelatedTree = ParseTree(
            "namespace Consumer; internal static class Unrelated { public const int Value = 1; }");
        CSharpCompilation compilation = CreateCompilation(
            RuntimeAssemblyName,
            includeAbstractions: true,
            includeFilters: true,
            includeFilterRuntimeReference: false,
            unrelatedTree);

        GeneratorDriver driver = CreateDriver(trackIncrementalSteps: true);
        driver = driver.RunGenerators(compilation);

        SyntaxTree changedUnrelatedTree = ParseTree(
            "namespace Consumer; internal static class Unrelated { /* trivia */ public const int Value = 1; }");
        CSharpCompilation changedCompilation = compilation.ReplaceSyntaxTree(unrelatedTree, changedUnrelatedTree);
        driver = driver.RunGenerators(changedCompilation);

        var outputs = TrackedOutputs(driver.GetRunResult(), "FilterSchemaBuiltInDiscovery");
        AssertEx.True(outputs.Length > 0, "FilterSchemaDiscovery produced tracked output");
        AssertEx.True(
            outputs.All(static output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
            "FilterSchemaDiscovery stayed cached for unrelated compilation change. Reasons: " +
                string.Join(", ", outputs.Select(static output => output.Reason)));
    }

    private static void GeneratorCachesCurrentSchemaForUnrelatedCompilationChange()
    {
        SyntaxTree eventTree = PluginEventTree();
        SyntaxTree unrelatedTree = ParseTree(
            "namespace Consumer; internal static class Unrelated { public const int Value = 1; }");
        CSharpCompilation compilation = CreateCompilation(
            "Plugin.Schema.Cached",
            includeAbstractions: true,
            includeFilters: false,
            includeFilterRuntimeReference: true,
            eventTree,
            unrelatedTree);

        GeneratorDriver driver = CreateDriver(trackIncrementalSteps: true);
        driver = driver.RunGenerators(compilation);

        SyntaxTree changedUnrelatedTree = ParseTree(
            "namespace Consumer; internal static class Unrelated { /* trivia */ public const int Value = 1; }");
        CSharpCompilation changedCompilation = compilation.ReplaceSyntaxTree(unrelatedTree, changedUnrelatedTree);
        driver = driver.RunGenerators(changedCompilation);

        var outputs = TrackedOutputs(driver.GetRunResult(), "FilterSchemaCurrentDiscovery");
        AssertEx.True(outputs.Length > 0, "FilterSchemaCurrentDiscovery produced tracked output");
        AssertEx.True(
            outputs.All(static output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
            "FilterSchemaCurrentDiscovery stayed cached for unrelated compilation change. Reasons: " +
                string.Join(", ", outputs.Select(static output => output.Reason)));
    }

    private static GeneratorRun RunGenerator(
        string assemblyName,
        bool includeAbstractions,
        bool includeFilters,
        bool includeFilterRuntimeReference,
        params SyntaxTree[] extraTrees)
    {
        CSharpCompilation compilation = CreateCompilation(
            assemblyName,
            includeAbstractions,
            includeFilters,
            includeFilterRuntimeReference,
            extraTrees);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);

        return new GeneratorRun(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static GeneratorDriver CreateDriver(bool trackIncrementalSteps = false) =>
        CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: trackIncrementalSteps));

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        bool includeAbstractions,
        bool includeFilters,
        bool includeFilterRuntimeReference,
        params SyntaxTree[] extraTrees)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => includeAbstractions || Path.GetFileNameWithoutExtension(path) != "SiftQL.Abstractions")
            .Where(path => includeFilterRuntimeReference || Path.GetFileNameWithoutExtension(path) != "SiftQL")
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();

        if (includeAbstractions)
            AddReference(references, typeof(IFilterSubject).Assembly.Location);
        if (includeFilterRuntimeReference)
            AddReference(references, typeof(FilterSchema).Assembly.Location);

        var syntaxTrees = new List<SyntaxTree>
        {
            ParseTree("namespace Consumer; internal static class Marker { }"),
        };
        if (includeFilters)
            syntaxTrees.Add(ParseTree(FilterRuntimeStubSource.Text));
        syntaxTrees.AddRange(extraTrees);

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private static SyntaxTree ParseTree(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static SyntaxTree PluginEventTree() =>
        ParseTree("""
            using System;
            // removed: game-specific value types
            using SiftQL;

            namespace Plugin.Events;

            public enum PluginEventKind { Unknown, Hit }

            public sealed record PluginOwnedEvent(
                Guid EventId,
                long CharacterId,
                SkillRef Skill,
                PluginEventKind Kind,
                int[] Tokens) : IFilterSubject;
            """);

    private static string GeneratedSource(GeneratorRun run, string hintName) =>
        run.Result.Results[0].GeneratedSources.Single(source => source.HintName == hintName).SourceText.ToString();

    private static void AssertNoCompilationErrors(GeneratorRun run, string label)
    {
        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, label + " compilation errors: " + string.Join(" | ", errors.Take(8)));
    }

    private static (object Value, IncrementalStepRunReason Reason)[] TrackedOutputs(
        GeneratorDriverRunResult result,
        string stepName)
    {
        if (!result.Results[0].TrackedSteps.TryGetValue(stepName, out ImmutableArray<IncrementalGeneratorRunStep> steps))
            throw new InvalidOperationException($"Tracked step '{stepName}' was not recorded.");

        return steps.SelectMany(static step => step.Outputs).ToArray();
    }

    private sealed record GeneratorRun(
        GeneratorDriverRunResult Result,
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> Diagnostics);
}

using System.Collections.Immutable;
using System.Reflection;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Generators.Kernel;
using SiftQL.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SiftQL.Generators.Tests;

internal static class KernelCatalogSourceGeneratorTests
{
    private const string KernelHint = "SampleHost.ServerKernel.KernelCatalog.g.cs";

    public static void RunAll()
    {
        GeneratorEmitsTypedFacadeAndCompilesPartialExtensions();
        GeneratedCatalogRejectsUnknownSubject();
        GeneratorReportsInvalidCatalogShape();
        GeneratorReportsInvalidSubjectContract();
        GeneratorReportsDuplicateAliases();
        GeneratorCachesCatalogForUnrelatedCompilationChange();
    }

    private static void GeneratorEmitsTypedFacadeAndCompilesPartialExtensions()
    {
        GeneratorRun run = RunGenerator(ParseTree(ValidCatalogSource(includeOtherEvent: false)));
        string source = GeneratedSource(run, KernelHint);

        AssertEx.Contains("ForItemUsed()", source, "alias-based subject factory emitted");
        AssertEx.Contains("ForWorldTickEvent()", source, "default subject factory emitted");
        AssertEx.Contains("IsKnownSubject", source, "known subject guard emitted");
        AssertNoCompilationErrors(run, "generated kernel catalog");

        Assembly assembly = EmitAssembly(run);
        Type kernelType = assembly.GetType("SampleHost.ServerKernel", throwOnError: true)!;
        Type itemEventType = assembly.GetType("SampleHost.ItemUsedEvent", throwOnError: true)!;
        object query = kernelType.GetMethod("PositiveConsumableUse")!.Invoke(null, null)!;
        FilterExpression filter = (FilterExpression)query.GetType().GetProperty("Filter")!.GetValue(query)!;
        var compiled = FilterCompiler.Compile(itemEventType, filter);

        object match = Activator.CreateInstance(itemEventType, 100L, "consumable", 3, "north")!;
        object wrongKind = Activator.CreateInstance(itemEventType, 100L, "material", 3, "north")!;
        object zeroQuantity = Activator.CreateInstance(itemEventType, 100L, "consumable", 0, "north")!;

        AssertEx.True(compiled.Matches(match), "positive consumable use matched");
        AssertEx.True(!compiled.Matches(wrongKind), "wrong item kind rejected");
        AssertEx.True(!compiled.Matches(zeroQuantity), "zero quantity rejected");
    }

    private static void GeneratedCatalogRejectsUnknownSubject()
    {
        GeneratorRun run = RunGenerator(ParseTree(ValidCatalogSource(includeOtherEvent: true)));
        AssertNoCompilationErrors(run, "generated kernel catalog with other event");
        Assembly assembly = EmitAssembly(run);
        Type kernelType = assembly.GetType("SampleHost.ServerKernel", throwOnError: true)!;
        Type otherType = assembly.GetType("SampleHost.OtherEvent", throwOnError: true)!;

        Exception? exception = null;
        try
        {
            kernelType.GetMethod("For")!.MakeGenericMethod(otherType).Invoke(null, null);
        }
        catch (TargetInvocationException ex)
        {
            exception = ex.InnerException;
        }

        AssertEx.True(exception is ArgumentException, "unregistered subject rejected by generated facade");
    }

    private static void GeneratorReportsInvalidCatalogShape()
    {
        GeneratorRun run = RunGenerator(ParseTree("""
            using SiftQL;
            namespace SampleHost;

            [KernelCatalog]
            public sealed class ServerKernel;
            """));

        AssertHasDiagnostic(run, "SIFTKERNEL001");
    }

    private static void GeneratorReportsInvalidSubjectContract()
    {
        GeneratorRun run = RunGenerator(ParseTree("""
            using SiftQL;
            namespace SampleHost;

            [KernelCatalog]
            [KernelSubject(typeof(NotAFilterSubject))]
            public static partial class ServerKernel;

            public sealed record NotAFilterSubject(int Value);
            """));

        AssertHasDiagnostic(run, "SIFTKERNEL002");
    }

    private static void GeneratorReportsDuplicateAliases()
    {
        GeneratorRun run = RunGenerator(ParseTree("""
            using SiftQL;
            namespace SampleHost;

            [KernelCatalog]
            [KernelSubject(typeof(ItemUsedEvent), Alias = "Repeated")]
            [KernelSubject(typeof(WorldTickEvent), Alias = "Repeated")]
            public static partial class ServerKernel;

            public sealed record ItemUsedEvent(long ActorId) : IFilterSubject;
            public sealed record WorldTickEvent(long Tick) : IFilterSubject;
            """));

        AssertHasDiagnostic(run, "SIFTKERNEL004");
    }

    private static void GeneratorCachesCatalogForUnrelatedCompilationChange()
    {
        SyntaxTree catalogTree = ParseTree(ValidCatalogSource(includeOtherEvent: false));
        SyntaxTree unrelatedTree = ParseTree("namespace SampleHost; internal static class Other { public const int Value = 1; }");
        CSharpCompilation compilation = CreateCompilation(catalogTree, unrelatedTree);
        GeneratorDriver driver = CreateDriver(trackIncrementalSteps: true);
        driver = driver.RunGenerators(compilation);

        SyntaxTree changedUnrelatedTree = ParseTree("namespace SampleHost; internal static class Other { /* trivia */ public const int Value = 1; }");
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(unrelatedTree, changedUnrelatedTree));

        var outputs = TrackedOutputs(driver.GetRunResult(), "KernelCatalogDiscovery");
        AssertEx.True(outputs.Length > 0, "KernelCatalogDiscovery produced tracked output");
        AssertEx.True(
            outputs.All(static output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
            "KernelCatalogDiscovery stayed cached for unrelated compilation change. Reasons: " +
                string.Join(", ", outputs.Select(static output => output.Reason)));
    }

    private static string ValidCatalogSource(bool includeOtherEvent) =>
        $$"""
        using SiftQL;

        namespace SampleHost;

        [KernelCatalog]
        [KernelSubject(typeof(ItemUsedEvent), Alias = "ItemUsed")]
        [KernelSubject(typeof(WorldTickEvent))]
        public static partial class ServerKernel
        {
            public static QueryKernel<ItemUsedEvent> PositiveConsumableUse() =>
                For<ItemUsedEvent>()
                    .Consumables()
                    .Where(static ev => ev.Quantity > 0);
        }

        public static class ItemKernelExtensions
        {
            public static QueryKernel<ItemUsedEvent> Consumables(this QueryKernel<ItemUsedEvent> kernel) =>
                kernel.Where(static ev => ev.ItemKind == "consumable");
        }

        public sealed record ItemUsedEvent(long ActorId, string ItemKind, int Quantity, string Region) : IFilterSubject;
        public sealed record WorldTickEvent(long Tick, string Region) : IFilterSubject;
        {{(includeOtherEvent ? "public sealed record OtherEvent(int Value) : IFilterSubject;" : string.Empty)}}
        """;

    private static GeneratorRun RunGenerator(params SyntaxTree[] trees)
    {
        CSharpCompilation compilation = CreateCompilation(trees);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);

        return new GeneratorRun(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    private static GeneratorDriver CreateDriver(bool trackIncrementalSteps = false) =>
        CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new KernelCatalogSourceGenerator().AsSourceGenerator()),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: trackIncrementalSteps));

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees)
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(IFilterSubject).Assembly.Location);
        AddReference(references, typeof(FilterExpression).Assembly.Location);

        return CSharpCompilation.Create(
            "KernelCatalog.Tests",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }

    private static SyntaxTree ParseTree(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static string GeneratedSource(GeneratorRun run, string hintName) =>
        run.Result.Results[0].GeneratedSources.Single(source => source.HintName == hintName).SourceText.ToString();

    private static Assembly EmitAssembly(GeneratorRun run)
    {
        using var pe = new MemoryStream();
        EmitResult emit = run.OutputCompilation.Emit(pe);
        AssertEx.True(emit.Success, "generated assembly emitted: " + string.Join(" | ", emit.Diagnostics));
        return Assembly.Load(pe.ToArray());
    }

    private static void AssertNoCompilationErrors(GeneratorRun run, string label)
    {
        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, label + " compilation errors: " + string.Join(" | ", errors.Take(8)));
    }

    private static void AssertHasDiagnostic(GeneratorRun run, string diagnosticId) =>
        AssertEx.True(
            run.Diagnostics.Any(diagnostic => diagnostic.Id == diagnosticId),
            $"Expected generator diagnostic '{diagnosticId}'. Actual: {string.Join(", ", run.Diagnostics.Select(diagnostic => diagnostic.Id))}");

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

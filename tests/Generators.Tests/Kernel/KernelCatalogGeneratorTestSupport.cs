using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using SiftQL.Expressions;
using SiftQL.Generators.Kernel;

namespace SiftQL.Generators.Tests;

internal static class KernelCatalogGeneratorTestSupport
{
    public static GeneratorRun RunGenerator(params SyntaxTree[] trees)
    {
        CSharpCompilation compilation = CreateCompilation(trees);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);

        return new GeneratorRun(driver.GetRunResult(), outputCompilation, diagnostics);
    }

    public static GeneratorDriver CreateDriver(bool trackIncrementalSteps = false) =>
        CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new KernelCatalogSourceGenerator().AsSourceGenerator()),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: trackIncrementalSteps));

    public static CSharpCompilation CreateCompilation(params SyntaxTree[] trees)
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

    public static SyntaxTree ParseTree(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    public static string GeneratedSource(GeneratorRun run, string hintName) =>
        run.Result.Results[0].GeneratedSources.Single(source => source.HintName == hintName).SourceText.ToString();

    public static Assembly EmitAssembly(GeneratorRun run)
    {
        using var pe = new MemoryStream();
        EmitResult emit = run.OutputCompilation.Emit(pe);
        AssertEx.True(emit.Success, "generated assembly emitted: " + string.Join(" | ", emit.Diagnostics));
        return Assembly.Load(pe.ToArray());
    }

    public static void AssertNoCompilationErrors(GeneratorRun run, string label)
    {
        Diagnostic[] errors = run.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AssertEx.Equal(0, errors.Length, label + " compilation errors: " + string.Join(" | ", errors.Take(8)));
    }

    public static void AssertHasDiagnostic(GeneratorRun run, string diagnosticId) =>
        AssertEx.True(
            run.Diagnostics.Any(diagnostic => diagnostic.Id == diagnosticId),
            $"Expected generator diagnostic '{diagnosticId}'. Actual: {string.Join(", ", run.Diagnostics.Select(diagnostic => diagnostic.Id))}");

    public static (object Value, IncrementalStepRunReason Reason)[] TrackedOutputs(
        GeneratorDriverRunResult result,
        string stepName)
    {
        if (!result.Results[0].TrackedSteps.TryGetValue(stepName, out ImmutableArray<IncrementalGeneratorRunStep> steps))
            throw new InvalidOperationException($"Tracked step '{stepName}' was not recorded.");

        return steps.SelectMany(static step => step.Outputs).ToArray();
    }

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }
}

internal sealed record GeneratorRun(
    GeneratorDriverRunResult Result,
    Compilation OutputCompilation,
    ImmutableArray<Diagnostic> Diagnostics);

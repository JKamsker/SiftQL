using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Projection;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace SiftQL.Generators.Tests;

public enum GeneratedExecutionMode
{
    Interpreted,
    Compiled,
    GeneratedHot,
}

internal static class GeneratedModeMatrixSupport
{
    public static TheoryData<GeneratedExecutionMode> Modes =>
    [
        GeneratedExecutionMode.Interpreted,
        GeneratedExecutionMode.Compiled,
        GeneratedExecutionMode.GeneratedHot,
    ];

    public static GeneratedModeContext LoadContext(
        GeneratedExecutionMode mode,
        string assemblyName,
        SyntaxTree eventTree,
        string eventTypeName,
        string label,
        params HotCompilationManifestEntry[] entries)
    {
        IDisposable scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        LoadedHotProvider? loaded = null;
        try
        {
            Assembly assembly;
            if (mode == GeneratedExecutionMode.GeneratedHot)
            {
                string manifest = ManifestJson(entries);
                (Compilation output, ImmutableArray<Diagnostic> diagnostics) = RunGenerator(
                    assemblyName,
                    eventTree,
                    new InMemoryAdditionalText("matrix.siftql-hot.json", manifest));
                Assert.Empty(diagnostics);
                AssertNoCompilationErrors(output);
                loaded = HotProviderTestLoader.Load(output, assemblyName, manifest, label);
                assembly = loaded.Assembly;
            }
            else
            {
                (Compilation output, ImmutableArray<Diagnostic> diagnostics) = RunGenerator(
                    assemblyName,
                    eventTree);
                Assert.Empty(diagnostics);
                AssertNoCompilationErrors(output);
                assembly = EmitAndLoad(output, label);
                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            }

            Type eventType = assembly.GetType(eventTypeName, throwOnError: true)!;
            return new GeneratedModeContext(scope, loaded, eventType);
        }
        catch
        {
            loaded?.Dispose();
            scope.Dispose();
            throw;
        }
    }

    public static FilterCompilerOptions FilterOptions(GeneratedExecutionMode mode) =>
        mode == GeneratedExecutionMode.Compiled
            ? FilterCompilerOptions.Immediate
            : FilterCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.FromDays(1),
                TieredPromotionMinimumEvaluations = int.MaxValue,
            };

    public static ProjectionCompilerOptions ProjectionOptions(GeneratedExecutionMode mode) =>
        mode == GeneratedExecutionMode.Compiled
            ? ProjectionCompilerOptions.Immediate
            : ProjectionCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.FromDays(1),
                TieredPromotionMinimumOperations = int.MaxValue,
            };

    public static EventPipelineCompilerOptions PipelineOptions(GeneratedExecutionMode mode) =>
        new()
        {
            FilterOptions = FilterOptions(mode),
            ProjectionOptions = ProjectionOptions(mode),
        };

    public static HotCompilationManifestEntry FilterEntry(
        string subjectType,
        FilterExpression expression) =>
        Entry(
            "filter",
            subjectType,
            FilterExpressionFingerprint.Create(expression),
            JsonSerializer.SerializeToElement(expression));

    public static HotCompilationManifestEntry ProjectionEntry(
        string subjectType,
        EventProjectionExpression projection) =>
        Entry(
            "projection",
            subjectType,
            ProjectionExpressionFingerprint.Create(projection),
            JsonSerializer.SerializeToElement(projection));

    public static string Subject(string eventTypeName, string assemblyName) =>
        eventTypeName + ", " + assemblyName;

    public static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        string assemblyName,
        SyntaxTree eventTree,
        params AdditionalText[] additionalTexts)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, eventTree);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: additionalTexts.ToImmutableArray(),
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

    private static Assembly EmitAndLoad(Compilation output, string label)
    {
        using var pe = new MemoryStream();
        var emit = output.Emit(pe);
        AssertEx.True(emit.Success, label + " emitted: " + string.Join(" | ", emit.Diagnostics));
        return Assembly.Load(pe.ToArray());
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

internal sealed class GeneratedModeContext : IDisposable
{
    private readonly IDisposable _scope;
    private readonly LoadedHotProvider? _provider;

    public GeneratedModeContext(IDisposable scope, LoadedHotProvider? provider, Type eventType)
    {
        _scope = scope;
        _provider = provider;
        EventType = eventType;
    }

    public Type EventType { get; }

    public Assembly Assembly => EventType.Assembly;

    public void Dispose()
    {
        _provider?.Dispose();
        _scope.Dispose();
    }
}

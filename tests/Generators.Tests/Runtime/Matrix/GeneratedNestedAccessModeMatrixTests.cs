using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Generators.Schema;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratedNestedAccessModeMatrixTests
{
    public static TheoryData<ExecutionMode> Modes =>
        [ExecutionMode.Interpreted, ExecutionMode.Compiled, ExecutionMode.GeneratedHot];

    [Theory]
    [MemberData(nameof(Modes))]
    public void NestedArrayContainsReturnsFalseWhenParentIsNull(ExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.NestedContains." + mode;
        FilterExpression filter = FilterExpression.Contains(
            "Location.Tags",
            FilterValue.From("rare"));
        using var context = LoadContext(mode, assemblyName, FilterEntry(Subject(assemblyName), filter));
        Type eventType = context.EventType;

        CompiledKernel kernel = FilterCompiler.Compile(eventType, filter, FilterOptions(mode));

        Assert.Equal(mode == ExecutionMode.Interpreted, kernel.IsTiered);
        Assert.False(kernel.Matches(Event(eventType, location: null)));
        Assert.False(kernel.Matches(Event(eventType, Location(eventType, "AT", score: 10, ["common"]))));
        Assert.True(kernel.Matches(Event(eventType, Location(eventType, "AT", score: 10, ["rare"]))));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void OrFilterReturnsSameMatchesWhenNestedParentIsNull(ExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.NestedOr." + mode;
        FilterExpression filter = FilterExpression.Or(
            FilterExpression.Compare("Location.Country", FilterOperator.Equal, FilterValue.From("AT")),
            FilterExpression.Compare("Location.Score", FilterOperator.GreaterThan, FilterValue.From(80L)));
        using var context = LoadContext(mode, assemblyName, FilterEntry(Subject(assemblyName), filter));
        Type eventType = context.EventType;

        CompiledKernel kernel = FilterCompiler.Compile(eventType, filter, FilterOptions(mode));

        Assert.Equal(mode == ExecutionMode.Interpreted, kernel.IsTiered);
        Assert.False(kernel.Matches(Event(eventType, location: null)));
        Assert.False(kernel.Matches(Event(eventType, Location(eventType, "DE", score: 10, ["common"]))));
        Assert.True(kernel.Matches(Event(eventType, Location(eventType, "AT", score: 10, ["common"]))));
        Assert.True(kernel.Matches(Event(eventType, Location(eventType, "DE", score: 90, ["common"]))));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task NestedProjectionWritesNullWhenParentIsNull(ExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.NestedProjection." + mode;
        EventProjectionExpression projection = EventProjectionExpression.Select(
            "Location.Country",
            "Location.Score");
        using var context = LoadContext(mode, assemblyName, ProjectionEntry(Subject(assemblyName), projection));
        Type eventType = context.EventType;

        CompiledProjection<object> compiled = ProjectionCompiler.Compile<object>(
            eventType,
            projection,
            RejectInclude,
            ProjectionOptions(mode));
        ProjectedEvent nullParent = await compiled.ProjectAsync(
            Event(eventType, location: null),
            new object(),
            CancellationToken.None);
        ProjectedEvent presentParent = await compiled.ProjectAsync(
            Event(eventType, Location(eventType, "AT", score: 7, ["rare"])),
            new object(),
            CancellationToken.None);

        Assert.Equal(mode == ExecutionMode.Interpreted, compiled.IsTiered);
        Assert.Equal(ProjectedEventValueKind.Null, nullParent.Field("Location.Country").Kind);
        Assert.Equal(ProjectedEventValueKind.Null, nullParent.Field("Location.Score").Kind);
        Assert.Equal("AT", presentParent.Field("Location.Country").String);
        Assert.Equal(7, presentParent.Field("Location.Score").Integer);
    }

    private static LoadedContext LoadContext(
        ExecutionMode mode,
        string assemblyName,
        params HotCompilationManifestEntry[] entries)
    {
        var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        string manifest = ManifestJson(entries);
        Assembly assembly;
        LoadedHotProvider? loaded = null;
        if (mode == ExecutionMode.GeneratedHot)
        {
            (Compilation output, ImmutableArray<Diagnostic> diagnostics) = RunGenerator(assemblyName, manifest);
            Assert.Empty(diagnostics);
            AssertNoCompilationErrors(output);
            loaded = HotProviderTestLoader.Load(output, assemblyName, manifest, "generated nested access matrix");
            assembly = loaded.Assembly;
        }
        else
        {
            (Compilation output, ImmutableArray<Diagnostic> diagnostics) = RunGenerator(assemblyName);
            Assert.Empty(diagnostics);
            assembly = EmitAndLoad(output, "generated nested access schema");
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        }

        return new LoadedContext(scope, loaded, assembly.GetType("Plugin.Events.PlayerMovedEvent", throwOnError: true)!);
    }

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        string assemblyName,
        string? manifest = null)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, EventTree());
        var additionalTexts = manifest is null
            ? ImmutableArray<AdditionalText>.Empty
            : ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText("nested.siftql-hot.json", manifest));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            additionalTexts: additionalTexts,
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> diagnostics);
        return (outputCompilation, diagnostics);
    }

    private static SyntaxTree EventTree() =>
        CSharpSyntaxTree.ParseText("""
            #nullable enable
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record PlayerLocation(string Country, int Score, string[] Tags);
            public sealed record PlayerMovedEvent(
                Guid EventId,
                PlayerLocation Location) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static string ManifestJson(params HotCompilationManifestEntry[] entries) =>
        JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries = entries,
        });

    private static HotCompilationManifestEntry FilterEntry(
        string subjectType,
        FilterExpression expression) =>
        Entry(
            "filter",
            subjectType,
            FilterExpressionFingerprint.Create(expression),
            JsonSerializer.SerializeToElement(expression));

    private static HotCompilationManifestEntry ProjectionEntry(
        string subjectType,
        EventProjectionExpression projection) =>
        Entry(
            "projection",
            subjectType,
            ProjectionExpressionFingerprint.Create(projection),
            JsonSerializer.SerializeToElement(projection));

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

    private static string Subject(string assemblyName) =>
        "Plugin.Events.PlayerMovedEvent, " + assemblyName;

    private static FilterCompilerOptions FilterOptions(ExecutionMode mode) =>
        mode == ExecutionMode.Compiled
            ? FilterCompilerOptions.Immediate
            : FilterCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.FromDays(1),
                TieredPromotionMinimumEvaluations = int.MaxValue,
            };

    private static ProjectionCompilerOptions ProjectionOptions(ExecutionMode mode) =>
        mode == ExecutionMode.Compiled
            ? ProjectionCompilerOptions.Immediate
            : ProjectionCompilerOptions.Tiered with
            {
                TieredPromotionMinimumAge = TimeSpan.FromDays(1),
                TieredPromotionMinimumOperations = int.MaxValue,
            };

    private static object Event(Type eventType, object? location) =>
        Activator.CreateInstance(eventType, Guid.NewGuid(), location)!;

    private static object Location(Type eventType, string country, int score, string[] tags)
    {
        Type locationType = eventType.Assembly.GetType("Plugin.Events.PlayerLocation", throwOnError: true)!;
        return Activator.CreateInstance(locationType, country, score, tags)!;
    }

    private static CompiledProjection<object>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

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

    public enum ExecutionMode
    {
        Interpreted,
        Compiled,
        GeneratedHot,
    }

    private sealed class LoadedContext : IDisposable
    {
        private readonly IDisposable _scope;
        private readonly LoadedHotProvider? _provider;

        public LoadedContext(IDisposable scope, LoadedHotProvider? provider, Type eventType)
        {
            _scope = scope;
            _provider = provider;
            EventType = eventType;
        }

        public Type EventType { get; }

        public void Dispose()
        {
            _provider?.Dispose();
            _scope.Dispose();
        }
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }
}

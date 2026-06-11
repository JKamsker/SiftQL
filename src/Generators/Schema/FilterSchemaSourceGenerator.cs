using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SiftQL.Generators.Hot;

namespace SiftQL.Generators.Schema;

[Generator(LanguageNames.CSharp)]
public sealed class FilterSchemaSourceGenerator : IIncrementalGenerator
{
    private const string RuntimeAssembly = "SiftQL";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var builtInSchemas = context.CompilationProvider
            .Select(static (compilation, ct) =>
                compilation.Assembly.Name == RuntimeAssembly
                    ? new GeneratedProvider(FilterSchemaDiscovery.DiscoverBuiltIn(compilation, ct), Emit: true)
                    : new GeneratedProvider(EquatableArray<GeneratedSchema>.Empty, Emit: false))
            .WithTrackingName("FilterSchemaBuiltInDiscovery");

        context.RegisterSourceOutput(builtInSchemas, static (ctx, discovered) =>
        {
            if (!discovered.Emit)
                return;

            string source = FilterSchemaEmitter.Emit(
                discovered.Schemas,
                "GeneratedFilterSchemaProvider",
                registerProvider: false);
            ctx.AddSource("GeneratedFilterSchemaProvider.g.cs", SourceText.From(source, Encoding.UTF8));
        });

        var currentSchemas = context.SyntaxProvider
            .CreateSyntaxProvider(
                FilterSchemaDiscovery.IsCurrentCandidate,
                FilterSchemaDiscovery.DiscoverCurrent)
            .Collect()
            .Select(static (schemas, _) => FilterSchemaDiscovery.SortCurrent(schemas))
            .WithTrackingName("FilterSchemaCurrentDiscovery");

        context.RegisterSourceOutput(currentSchemas, static (ctx, discovered) =>
        {
            if (discovered.Count == 0)
                return;

            string source = FilterSchemaEmitter.Emit(
                discovered,
                CurrentProviderName(discovered),
                registerProvider: true);
            ctx.AddSource("GeneratedCurrentFilterSchemaProvider.g.cs", SourceText.From(source, Encoding.UTF8));
        });

        var hotManifests = context.AdditionalTextsProvider
            .Where(static text => HotManifestParser.IsCandidate(text.Path))
            .Select(static (text, ct) => HotManifestParser.Parse(text, ct))
            .WithTrackingName("HotManifestParse");

        var hotProviders = hotManifests
            .Collect()
            .Combine(context.CompilationProvider)
            .SelectMany(static (pair, ct) => ResolveHotProviders(pair.Right, pair.Left, ct))
            .WithTrackingName("HotProviderResolve");

        context.RegisterSourceOutput(hotProviders, static (ctx, provider) =>
        {
            foreach (HotProviderDiagnostic diagnostic in provider.Diagnostics)
                ctx.ReportDiagnostic(HotProviderDiagnostics.Create(diagnostic));

            if (provider.Diagnostics.Count != 0 || provider.Entries.Count == 0)
                return;

            string source = HotProviderEmitter.Emit(provider);
            ctx.AddSource(provider.HintName, SourceText.From(source, Encoding.UTF8));
        });
    }

    private static IEnumerable<HotProviderSource> ResolveHotProviders(
        Compilation compilation,
        ImmutableArray<HotManifestParseResult> manifests,
        CancellationToken cancellationToken)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < manifests.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HotManifestParseResult manifest = manifests[i];
            if (IsDuplicateValidManifest(manifest, hashes))
                continue;

            yield return HotProviderResolver.Resolve(compilation, manifest, cancellationToken);
        }
    }

    private static bool IsDuplicateValidManifest(
        HotManifestParseResult manifest,
        HashSet<string> hashes) =>
        manifest.Diagnostics.Count == 0 &&
        !string.IsNullOrEmpty(manifest.ManifestHash) &&
        !hashes.Add(manifest.ManifestHash);

    private static string CurrentProviderName(EquatableArray<GeneratedSchema> schemas)
    {
        var identity = new StringBuilder();
        for (int i = 0; i < schemas.Count; i++)
            identity.Append(schemas[i].TypeName).Append('|');

        return "GeneratedCurrentFilterSchemaProvider_" + StableHash(identity.ToString());
    }

    private static string StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char ch in text)
        {
            hash ^= ch;
            hash *= 16777619;
        }

        return hash.ToString("X8");
    }
}

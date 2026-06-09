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
                "GeneratedCurrentFilterSchemaProvider",
                registerProvider: true);
            ctx.AddSource("GeneratedCurrentFilterSchemaProvider.g.cs", SourceText.From(source, Encoding.UTF8));
        });

        var hotManifests = context.AdditionalTextsProvider
            .Where(static text => HotManifestParser.IsCandidate(text.Path))
            .Select(static (text, ct) => HotManifestParser.Parse(text, ct))
            .WithTrackingName("HotManifestParse");

        var hotProviders = hotManifests
            .Combine(context.CompilationProvider)
            .Select(static (pair, ct) => HotProviderResolver.Resolve(pair.Right, pair.Left, ct))
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
}

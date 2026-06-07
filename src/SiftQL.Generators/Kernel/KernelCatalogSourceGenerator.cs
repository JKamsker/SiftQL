using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Kernel;

[Generator(LanguageNames.CSharp)]
public sealed class KernelCatalogSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource(KernelCatalogAttributes.HintName, KernelCatalogAttributes.CreateSourceText()));

        var catalogs = context.SyntaxProvider.ForAttributeWithMetadataName(
                KernelCatalogDiscovery.CatalogAttributeName,
                KernelCatalogDiscovery.IsCandidate,
                KernelCatalogDiscovery.Discover)
            .WithTrackingName("KernelCatalogDiscovery");

        context.RegisterSourceOutput(catalogs, static (ctx, result) =>
        {
            foreach (KernelCatalogDiagnostic diagnostic in result.Diagnostics)
                ctx.ReportDiagnostic(KernelCatalogDiagnostics.Create(diagnostic));

            if (result.Catalog is null)
                return;

            string hintName = HintName(result.Catalog);
            string source = KernelCatalogEmitter.Emit(result.Catalog);
            ctx.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        });
    }

    private static string HintName(KernelCatalogModel catalog)
    {
        string name = string.IsNullOrEmpty(catalog.NamespaceName)
            ? catalog.Name
            : catalog.NamespaceName + "." + catalog.Name;
        return name + ".KernelCatalog.g.cs";
    }
}

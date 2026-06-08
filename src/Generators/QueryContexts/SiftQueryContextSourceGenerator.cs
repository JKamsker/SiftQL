using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.QueryContexts;

[Generator(LanguageNames.CSharp)]
public sealed class SiftQueryContextSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contexts = context.SyntaxProvider.ForAttributeWithMetadataName(
                QueryContextDiscovery.ContextAttributeName,
                QueryContextDiscovery.IsCandidate,
                QueryContextDiscovery.Discover)
            .WithTrackingName("QueryContextDiscovery")
            .Collect();

        context.RegisterSourceOutput(contexts, static (ctx, results) =>
        {
            foreach (QueryContextResult result in results)
            {
                foreach (QueryContextDiagnostic diagnostic in result.Diagnostics)
                    ctx.ReportDiagnostic(QueryContextDiagnostics.Create(diagnostic));
            }

            QueryContextModel[] models = results
                .Select(static result => result.Context)
                .Where(static model => model is not null)
                .Select(static model => model!)
                .ToArray();
            HashSet<string> duplicateIds = DuplicateContextIds(models);
            foreach (string duplicateId in duplicateIds)
            {
                ctx.ReportDiagnostic(QueryContextDiagnostics.Create(new(
                    QueryContextDiagnostics.DuplicateContextId,
                    $"Query context id '{duplicateId}' is used more than once in this compilation.")));
            }

            foreach (QueryContextModel model in models)
            {
                if (duplicateIds.Contains(model.ContextId))
                    continue;

                ctx.AddSource(
                    HintName(model),
                    SourceText.From(QueryContextEmitter.Emit(model), Encoding.UTF8));
            }
        });
    }

    private static HashSet<string> DuplicateContextIds(IReadOnlyCollection<QueryContextModel> contexts)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (QueryContextModel context in contexts)
            counts[context.ContextId] = counts.TryGetValue(context.ContextId, out int count) ? count + 1 : 1;

        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> item in counts)
        {
            if (item.Value > 1)
                duplicates.Add(item.Key);
        }

        return duplicates;
    }

    private static string HintName(QueryContextModel context)
    {
        string name = string.IsNullOrEmpty(context.NamespaceName)
            ? context.InterfaceName
            : context.NamespaceName + "." + context.InterfaceName;
        return name + ".SiftQueryContext.g.cs";
    }
}

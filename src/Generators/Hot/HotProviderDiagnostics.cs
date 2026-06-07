using Microsoft.CodeAnalysis;

namespace SiftQL.Generators.Hot;

internal static class HotProviderDiagnostics
{
    private const string Category = "SiftQL.Hot";

    public static Diagnostic Create(HotProviderDiagnostic diagnostic) =>
        Diagnostic.Create(
            new DiagnosticDescriptor(
                diagnostic.Id,
                "Invalid hot filter manifest",
                "{0}",
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true),
            Location.None,
            string.IsNullOrWhiteSpace(diagnostic.Path)
                ? diagnostic.Message
                : diagnostic.Path + ": " + diagnostic.Message);
}

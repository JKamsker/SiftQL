using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators;

internal static class CSharpIdentifier
{
    public static string EscapePath(string path) =>
        string.Join(".", path.Split('.').Select(Escape));

    private static string Escape(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ||
        SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;
}

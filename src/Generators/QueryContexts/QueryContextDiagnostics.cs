using Microsoft.CodeAnalysis;

namespace SiftQL.Generators.QueryContexts;

#pragma warning disable RS2008

internal static class QueryContextDiagnostics
{
    public const string InvalidContextShape = "SIFTQCTX001";
    public const string DuplicateContextId = "SIFTQCTX002";
    public const string InvalidMethodShape = "SIFTQCTX003";
    public const string DuplicateMethodId = "SIFTQCTX004";
    public const string UnsupportedDefaultValue = "SIFTQCTX005";
    public const string HelperNameCollision = "SIFTQCTX006";

    private static readonly DiagnosticDescriptor s_invalidContextShape = Create(
        InvalidContextShape,
        "Invalid query context contract");
    private static readonly DiagnosticDescriptor s_duplicateContextId = Create(
        DuplicateContextId,
        "Duplicate query context id");
    private static readonly DiagnosticDescriptor s_invalidMethodShape = Create(
        InvalidMethodShape,
        "Invalid query context method");
    private static readonly DiagnosticDescriptor s_duplicateMethodId = Create(
        DuplicateMethodId,
        "Duplicate query context method id");
    private static readonly DiagnosticDescriptor s_unsupportedDefaultValue = Create(
        UnsupportedDefaultValue,
        "Unsupported query context default value");
    private static readonly DiagnosticDescriptor s_helperNameCollision = Create(
        HelperNameCollision,
        "Query context helper name collision");

    public static Diagnostic Create(QueryContextDiagnostic diagnostic) =>
        Diagnostic.Create(Descriptor(diagnostic.Id), Location.None, diagnostic.Message);

    private static DiagnosticDescriptor Descriptor(string id) =>
        id switch
        {
            InvalidContextShape => s_invalidContextShape,
            DuplicateContextId => s_duplicateContextId,
            InvalidMethodShape => s_invalidMethodShape,
            DuplicateMethodId => s_duplicateMethodId,
            UnsupportedDefaultValue => s_unsupportedDefaultValue,
            HelperNameCollision => s_helperNameCollision,
            _ => s_invalidContextShape,
        };

    private static DiagnosticDescriptor Create(string id, string title) =>
        new(
            id,
            title,
            "{0}",
            "SiftQL.QueryContexts",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
}

#pragma warning restore RS2008

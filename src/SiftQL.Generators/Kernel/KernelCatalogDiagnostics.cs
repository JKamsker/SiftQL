using Microsoft.CodeAnalysis;

namespace SiftQL.Generators.Kernel;

#pragma warning disable RS2008

internal static class KernelCatalogDiagnostics
{
    public const string InvalidCatalogShape = "SIFTKERNEL001";
    public const string InvalidSubject = "SIFTKERNEL002";
    public const string DuplicateSubject = "SIFTKERNEL003";
    public const string DuplicateMethodName = "SIFTKERNEL004";
    public const string InvalidAlias = "SIFTKERNEL005";

    private static readonly DiagnosticDescriptor s_invalidCatalogShape = Create(
        InvalidCatalogShape,
        "Invalid kernel catalog shape");
    private static readonly DiagnosticDescriptor s_invalidSubject = Create(
        InvalidSubject,
        "Invalid kernel subject");
    private static readonly DiagnosticDescriptor s_duplicateSubject = Create(
        DuplicateSubject,
        "Duplicate kernel subject");
    private static readonly DiagnosticDescriptor s_duplicateMethodName = Create(
        DuplicateMethodName,
        "Duplicate generated kernel method");
    private static readonly DiagnosticDescriptor s_invalidAlias = Create(
        InvalidAlias,
        "Invalid kernel subject alias");

    public static Diagnostic Create(KernelCatalogDiagnostic diagnostic) =>
        Diagnostic.Create(Descriptor(diagnostic.Id), Location.None, diagnostic.Message);

    private static DiagnosticDescriptor Descriptor(string id) =>
        id switch
        {
            InvalidCatalogShape => s_invalidCatalogShape,
            InvalidSubject => s_invalidSubject,
            DuplicateSubject => s_duplicateSubject,
            DuplicateMethodName => s_duplicateMethodName,
            InvalidAlias => s_invalidAlias,
            _ => Create(id, "Kernel catalog generator diagnostic"),
        };

    private static DiagnosticDescriptor Create(string id, string title) =>
        new(
            id,
            title,
            "{0}",
            "SiftQL.Kernel",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
}

#pragma warning restore RS2008

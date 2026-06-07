namespace SiftQL.Generators.Kernel;

internal sealed record KernelCatalogResult(
    KernelCatalogModel? Catalog,
    EquatableArray<KernelCatalogDiagnostic> Diagnostics);

internal sealed record KernelCatalogModel(
    string NamespaceName,
    string Accessibility,
    string Name,
    EquatableArray<KernelCatalogSubject> Subjects);

internal sealed record KernelCatalogSubject(
    string TypeName,
    string DisplayName,
    string MethodName);

internal sealed record KernelCatalogDiagnostic(string Id, string Message);

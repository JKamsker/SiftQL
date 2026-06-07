using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SiftQL.Generators.Kernel;

internal static class KernelCatalogDiscovery
{
    public const string CatalogAttributeName = "SiftQL.KernelCatalogAttribute";
    private const string SubjectAttributeName = "SiftQL.KernelSubjectAttribute";
    private static readonly SymbolDisplayFormat s_format = SymbolDisplayFormat.FullyQualifiedFormat;

    public static bool IsCandidate(SyntaxNode node, CancellationToken _) =>
        node is ClassDeclarationSyntax;

    public static KernelCatalogResult Discover(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = ImmutableArray.CreateBuilder<KernelCatalogDiagnostic>();
        var catalog = (INamedTypeSymbol)context.TargetSymbol;
        ValidateCatalog(catalog, diagnostics);

        INamedTypeSymbol? contract = SubjectContract(context);
        EquatableArray<KernelCatalogSubject> subjects = DiscoverSubjects(
            catalog,
            contract,
            diagnostics,
            cancellationToken);

        if (diagnostics.Count != 0)
            return new(null, new EquatableArray<KernelCatalogDiagnostic>(diagnostics.ToImmutable()));

        return new(
            new KernelCatalogModel(
                NamespaceName(catalog),
                AccessibilityText(catalog),
                catalog.Name,
                contract?.ToDisplayString(s_format),
                subjects),
            EquatableArray<KernelCatalogDiagnostic>.Empty);
    }

    private static void ValidateCatalog(
        INamedTypeSymbol catalog,
        ImmutableArray<KernelCatalogDiagnostic>.Builder diagnostics)
    {
        if (catalog is not { TypeKind: TypeKind.Class, IsStatic: true, IsGenericType: false } ||
            !IsPartial(catalog))
        {
            diagnostics.Add(new(
                KernelCatalogDiagnostics.InvalidCatalogShape,
                $"Kernel catalog '{catalog.ToDisplayString()}' must be a non-generic static partial class."));
        }

        if (catalog.ContainingType is not null)
        {
            diagnostics.Add(new(
                KernelCatalogDiagnostics.InvalidCatalogShape,
                $"Kernel catalog '{catalog.ToDisplayString()}' must be a top-level class."));
        }

        if (catalog.DeclaredAccessibility is
            not Microsoft.CodeAnalysis.Accessibility.Public and
            not Microsoft.CodeAnalysis.Accessibility.Internal)
        {
            diagnostics.Add(new(
                KernelCatalogDiagnostics.InvalidCatalogShape,
                $"Kernel catalog '{catalog.ToDisplayString()}' must be public or internal."));
        }
    }

    private static EquatableArray<KernelCatalogSubject> DiscoverSubjects(
        INamedTypeSymbol catalog,
        INamedTypeSymbol? contract,
        ImmutableArray<KernelCatalogDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        var subjects = ImmutableArray.CreateBuilder<KernelCatalogSubject>();
        var subjectNames = new HashSet<string>(StringComparer.Ordinal);
        var methodNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (AttributeData attribute in catalog.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSubjectAttribute(attribute))
                continue;

            AddSubject(attribute, contract, subjects, subjectNames, methodNames, diagnostics);
        }

        return new EquatableArray<KernelCatalogSubject>(
            subjects.OrderBy(static item => item.MethodName, StringComparer.Ordinal).ToImmutableArray());
    }

    private static void AddSubject(
        AttributeData attribute,
        INamedTypeSymbol? contract,
        ImmutableArray<KernelCatalogSubject>.Builder subjects,
        HashSet<string> subjectNames,
        HashSet<string> methodNames,
        ImmutableArray<KernelCatalogDiagnostic>.Builder diagnostics)
    {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not INamedTypeSymbol subject)
            return;

        string typeName = subject.ToDisplayString(s_format);
        if (!subjectNames.Add(typeName))
        {
            diagnostics.Add(new(
                KernelCatalogDiagnostics.DuplicateSubject,
                $"Kernel subject '{subject.ToDisplayString()}' is registered more than once."));
            return;
        }

        if (contract is not null && !InheritsOrImplements(subject, contract))
        {
            diagnostics.Add(new(
                KernelCatalogDiagnostics.InvalidSubject,
                $"Kernel subject '{subject.ToDisplayString()}' must inherit or implement '{contract.ToDisplayString()}'."));
            return;
        }

        string alias = Alias(attribute) ?? subject.Name;
        if (!SyntaxFacts.IsValidIdentifier(alias))
        {
            diagnostics.Add(new(
                KernelCatalogDiagnostics.InvalidAlias,
                $"Kernel subject alias '{alias}' is not a valid C# identifier."));
            return;
        }

        string methodName = "For" + alias;
        if (!methodNames.Add(methodName))
        {
            diagnostics.Add(new(
                KernelCatalogDiagnostics.DuplicateMethodName,
                $"Kernel subject alias '{alias}' produces duplicate method '{methodName}'."));
            return;
        }

        subjects.Add(new(typeName, subject.ToDisplayString(), methodName));
    }

    private static bool IsSubjectAttribute(AttributeData attribute) =>
        string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            SubjectAttributeName,
            StringComparison.Ordinal);

    private static INamedTypeSymbol? SubjectContract(
        GeneratorAttributeSyntaxContext context)
    {
        AttributeData? attribute = context.Attributes.FirstOrDefault(IsCatalogAttribute);
        if (attribute is null)
            return null;

        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == "SubjectContract")
                return argument.Value.Value as INamedTypeSymbol;
        }

        return null;
    }

    private static bool IsCatalogAttribute(AttributeData attribute) =>
        string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            CatalogAttributeName,
            StringComparison.Ordinal);

    private static string? Alias(AttributeData attribute)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == "Alias")
                return argument.Value.Value as string;
        }

        return null;
    }

    private static bool InheritsOrImplements(INamedTypeSymbol subject, INamedTypeSymbol contract)
    {
        for (INamedTypeSymbol? current = subject; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, contract))
                return true;
        }

        return subject.AllInterfaces.Any(item => SymbolEqualityComparer.Default.Equals(item, contract));
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences
            .Select(static item => item.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static string NamespaceName(INamedTypeSymbol type) =>
        type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();

    private static string AccessibilityText(INamedTypeSymbol type) =>
        type.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public ? "public" : "internal";
}

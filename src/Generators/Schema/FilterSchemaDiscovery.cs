using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SiftQL.Generators.Schema;

internal static class FilterSchemaDiscovery
{
    private const string AbstractionsAssembly = "SiftQL.Abstractions";
    private const string RuntimeAssembly = "SiftQL";
    private const string FilterSubjectName = "SiftQL.IFilterSubject";
    private const string RegistryName = "SiftQL.Schema.GeneratedFilterSchemaRegistry";
    private static readonly SymbolDisplayFormat s_format = SymbolDisplayFormat.FullyQualifiedFormat;

    public static EquatableArray<GeneratedSchema> DiscoverBuiltIn(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        IAssemblySymbol? abstractions = FindAbstractions(compilation);
        if (abstractions is null)
            return EquatableArray<GeneratedSchema>.Empty;

        INamedTypeSymbol? filterSubject = compilation.GetTypeByMetadataName(FilterSubjectName);
        var schemas = ImmutableArray.CreateBuilder<GeneratedSchema>();
        AddEligibleBuiltInTypes(schemas, abstractions.GlobalNamespace, filterSubject, cancellationToken);
        AddEligibleBuiltInTypes(schemas, compilation.Assembly.GlobalNamespace, filterSubject, cancellationToken);

        return Sort(schemas.ToImmutable());
    }

    public static bool IsCurrentCandidate(SyntaxNode node, CancellationToken _) =>
        node is TypeDeclarationSyntax { BaseList: not null } type &&
        type.Modifiers.Any(SyntaxKind.PublicKeyword);

    public static GeneratedSchema? DiscoverCurrent(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanEmitCurrentProvider(context.SemanticModel.Compilation) ||
            context.Node is not TypeDeclarationSyntax declaration)
        {
            return null;
        }

        INamedTypeSymbol? filterSubject = context.SemanticModel.Compilation.GetTypeByMetadataName(FilterSubjectName);
        if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol type ||
            !IsCurrentEligible(type, filterSubject))
        {
            return null;
        }

        return CreateSchema(type);
    }

    public static EquatableArray<GeneratedSchema> SortCurrent(ImmutableArray<GeneratedSchema?> schemas)
    {
        var builder = ImmutableArray.CreateBuilder<GeneratedSchema>(schemas.Length);
        foreach (GeneratedSchema? schema in schemas)
        {
            if (schema is not null)
                builder.Add(schema);
        }

        return Sort(builder.ToImmutable());
    }

    private static EquatableArray<GeneratedSchema> Sort(ImmutableArray<GeneratedSchema> schemas) =>
        new(schemas
            .OrderBy(static item => item.MetadataName, StringComparer.Ordinal)
            .ToImmutableArray());

    private static GeneratedSchema CreateSchema(INamedTypeSymbol type)
    {
        var fields = ImmutableArray.CreateBuilder<GeneratedField>();
        SchemaFieldDiscovery.AddProperties(fields, string.Empty, string.Empty, type, depth: 0);
        return new GeneratedSchema(
            type.ToDisplayString(s_format),
            type.ToDisplayString(),
            HelperName(type),
            new EquatableArray<GeneratedField>(fields.ToImmutable()));
    }

    private static IAssemblySymbol? FindAbstractions(Compilation compilation) =>
        compilation.Assembly.Name == AbstractionsAssembly
            ? compilation.Assembly
            : compilation.SourceModule.ReferencedAssemblySymbols
                .FirstOrDefault(static assembly => assembly.Name == AbstractionsAssembly);

    private static void AddEligibleBuiltInTypes(
        ImmutableArray<GeneratedSchema>.Builder schemas,
        INamespaceSymbol scope,
        INamedTypeSymbol? filterSubject,
        CancellationToken cancellationToken)
    {
        foreach (INamedTypeSymbol type in EnumerateTypes(scope, cancellationToken))
        {
            if (IsBuiltInEligible(type, filterSubject))
                schemas.Add(CreateSchema(type));
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(
        INamespaceSymbol scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (INamespaceOrTypeSymbol member in scope.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
            {
                foreach (INamedTypeSymbol child in EnumerateTypes(childNamespace, cancellationToken))
                    yield return child;
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (INamedTypeSymbol nested in EnumerateNestedTypes(type, cancellationToken))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (INamedTypeSymbol nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (INamedTypeSymbol child in EnumerateNestedTypes(nested, cancellationToken))
                yield return child;
        }
    }

    private static bool IsBuiltInEligible(INamedTypeSymbol type, INamedTypeSymbol? filterSubject) =>
        IsEligibleShape(type) && Implements(type, filterSubject);

    private static bool IsCurrentEligible(INamedTypeSymbol type, INamedTypeSymbol? filterSubject) =>
        IsEligibleShape(type) &&
        IsExternallyVisible(type) &&
        Implements(type, filterSubject);

    private static bool IsEligibleShape(INamedTypeSymbol type) =>
        type.DeclaredAccessibility == Accessibility.Public &&
        !type.IsGenericType &&
        !type.IsAbstract &&
        type.TypeKind is TypeKind.Class or TypeKind.Struct;

    private static bool IsExternallyVisible(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
                return false;
        }

        return true;
    }

    private static bool CanEmitCurrentProvider(Compilation compilation) =>
        compilation.Assembly.Name != RuntimeAssembly &&
        compilation.GetTypeByMetadataName(RegistryName) is not null;

    private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol? contract) =>
        contract is not null &&
        type.AllInterfaces.Any(item => SymbolEqualityComparer.Default.Equals(item, contract));

    private static string HelperName(INamedTypeSymbol type)
    {
        string display = type.ToDisplayString(s_format);
        string sanitized = string.Concat(display.Select(static ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        return sanitized + "_" + StableHash(display);
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

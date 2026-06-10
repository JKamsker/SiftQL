using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SiftQL.Generators.Schema;

internal static class SchemaCollectionFieldDiscovery
{
    private static readonly SymbolDisplayFormat s_format = SymbolDisplayFormat.FullyQualifiedFormat;

    public static bool TryAddProperties(
        ImmutableArray<GeneratedField>.Builder fields,
        string name,
        string access,
        ITypeSymbol collectionType,
        int depth)
    {
        if (!TryCollectionElement(collectionType, out ITypeSymbol? elementType) ||
            SchemaFieldDiscovery.TryScalar(elementType, out _) ||
            !SchemaFieldDiscovery.IsValueObject(elementType) ||
            elementType is not INamedTypeSymbol owner)
        {
            return false;
        }

        if (!SchemaFieldDiscovery.ContainsField(fields, name))
            fields.Add(CollectionField(name, access, elementType, collectionType));
        AddProperties(fields, name, access, owner, depth + 1);
        return true;
    }

    private static void AddProperties(
        ImmutableArray<GeneratedField>.Builder fields,
        string prefix,
        string accessPrefix,
        INamedTypeSymbol owner,
        int depth)
    {
        if (depth > 3)
            return;

        foreach (IPropertySymbol property in SchemaFieldDiscovery.EnumerateProperties(owner))
        {
            if (!SchemaFieldDiscovery.CanRead(property))
                continue;

            string name = prefix + "." + property.Name;
            if (SchemaFieldDiscovery.ContainsField(fields, name))
                continue;

            string access = accessPrefix + "." + property.Name;
            ITypeSymbol valueType = SchemaFieldDiscovery.UnwrapNullable(property.Type);
            if (SchemaFieldDiscovery.TryScalar(valueType, out GeneratedScalarKind scalarKind))
            {
                fields.Add(Field(name, access, valueType, property.Type, scalarKind));
                continue;
            }

            if (TryCollectionElement(property.Type, out ITypeSymbol? elementType))
            {
                if (SchemaFieldDiscovery.TryScalar(elementType, out scalarKind))
                {
                    fields.Add(Field(name, access, elementType, property.Type, scalarKind));
                    continue;
                }

                if (SchemaFieldDiscovery.IsValueObject(elementType) &&
                    elementType is INamedTypeSymbol nestedCollectionElement)
                {
                    AddProperties(fields, name, access, nestedCollectionElement, depth + 1);
                }

                continue;
            }

            if (SchemaFieldDiscovery.IsValueObject(valueType) &&
                valueType is INamedTypeSymbol nested)
            {
                AddProperties(fields, name, access, nested, depth + 1);
            }
        }
    }

    private static GeneratedField Field(
        string name,
        string access,
        ITypeSymbol valueType,
        ITypeSymbol propertyType,
        GeneratedScalarKind scalarKind) =>
        new(
            name,
            access,
            access,
            valueType.ToDisplayString(s_format),
            propertyType.ToDisplayString(s_format),
            GeneratedFieldKind.Array,
            scalarKind,
            SchemaFieldDiscovery.IsNullable(propertyType),
            AccessCanReturnNull: false,
            EmitsScalarAccessor: false,
            ArrayContainsMethod: null,
            UsesCollectionAccessor: true);

    private static GeneratedField CollectionField(
        string name,
        string access,
        ITypeSymbol elementType,
        ITypeSymbol collectionType) =>
        new(
            name,
            access,
            access,
            elementType.ToDisplayString(s_format),
            collectionType.ToDisplayString(s_format),
            GeneratedFieldKind.Array,
            GeneratedScalarKind.Object,
            SchemaFieldDiscovery.IsNullable(collectionType),
            AccessCanReturnNull: false,
            EmitsScalarAccessor: false,
            ArrayContainsMethod: null,
            UsesCollectionAccessor: true);

    private static bool TryCollectionElement(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            elementType = type;
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            elementType = SchemaFieldDiscovery.UnwrapNullable(array.ElementType);
            return true;
        }

        INamedTypeSymbol? enumerable = type.AllInterfaces
            .Concat(type is INamedTypeSymbol named ? [named] : [])
            .FirstOrDefault(static item => item.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        elementType = enumerable is null
            ? type
            : SchemaFieldDiscovery.UnwrapNullable(enumerable.TypeArguments[0]);
        return enumerable is not null;
    }
}

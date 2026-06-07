using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SiftQL.Generators.Schema;

internal static class SchemaFieldDiscovery
{
    private static readonly SymbolDisplayFormat s_format = SymbolDisplayFormat.FullyQualifiedFormat;

    public static void AddProperties(
        ImmutableArray<GeneratedField>.Builder fields,
        string prefix,
        string accessPrefix,
        INamedTypeSymbol owner,
        int depth)
    {
        if (depth > 3)
            return;

        foreach (IPropertySymbol property in EnumerateProperties(owner))
        {
            if (!CanRead(property))
                continue;

            string name = string.IsNullOrEmpty(prefix) ? property.Name : prefix + "." + property.Name;
            string access = string.IsNullOrEmpty(accessPrefix) ? property.Name : accessPrefix + "." + property.Name;
            ITypeSymbol valueType = UnwrapNullable(property.Type);
            if (TryScalar(valueType, out GeneratedScalarKind scalarKind))
            {
                fields.Add(Field(name, access, valueType, property.Type, GeneratedFieldKind.Scalar, scalarKind));
                continue;
            }

            if (TryCollectionElement(property.Type, out ITypeSymbol? elementType) &&
                TryScalar(elementType, out scalarKind))
            {
                fields.Add(Field(name, access, elementType, property.Type, GeneratedFieldKind.Array, scalarKind));
                continue;
            }

            if (!IsNullable(property.Type) && IsValueObject(valueType) && valueType is INamedTypeSymbol nested)
                AddProperties(fields, name, access, nested, depth + 1);
        }
    }

    private static bool CanRead(IPropertySymbol property) =>
        !property.IsStatic &&
        property.GetMethod is not null &&
        property.GetMethod.DeclaredAccessibility == Accessibility.Public &&
        property.Parameters.Length == 0;

    private static GeneratedField Field(
        string name,
        string access,
        ITypeSymbol valueType,
        ITypeSymbol propertyType,
        GeneratedFieldKind fieldKind,
        GeneratedScalarKind scalarKind) =>
        new(
            name,
            access,
            valueType.ToDisplayString(s_format),
            propertyType.ToDisplayString(s_format),
            fieldKind,
            scalarKind,
            IsNullable(propertyType),
            ArrayContainsMethod(propertyType, scalarKind));

    private static IEnumerable<IPropertySymbol> EnumerateProperties(INamedTypeSymbol owner)
    {
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = owner; current is not null; current = current.BaseType)
            hierarchy.Push(current);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (hierarchy.Count > 0)
        {
            foreach (IPropertySymbol property in hierarchy.Pop().GetMembers().OfType<IPropertySymbol>())
            {
                if (seen.Add(property.Name))
                    yield return property;
            }
        }
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? named.TypeArguments[0]
            : type;

    private static bool IsNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool TryCollectionElement(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            elementType = type;
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            elementType = UnwrapNullable(array.ElementType);
            return TryScalar(elementType, out _);
        }

        INamedTypeSymbol? enumerable = type.AllInterfaces
            .Concat(type is INamedTypeSymbol named ? [named] : [])
            .FirstOrDefault(static item => item.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        elementType = enumerable is null ? type : UnwrapNullable(enumerable.TypeArguments[0]);
        return enumerable is not null && TryScalar(elementType, out _);
    }

    private static bool TryScalar(ITypeSymbol type, out GeneratedScalarKind kind)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            kind = GeneratedScalarKind.Enum;
            return true;
        }

        kind = type.SpecialType switch
        {
            SpecialType.System_Boolean => GeneratedScalarKind.Boolean,
            SpecialType.System_String => GeneratedScalarKind.String,
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or
                SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or
                SpecialType.System_Double or SpecialType.System_Decimal => GeneratedScalarKind.Number,
            _ when type.ToDisplayString() == "System.Guid" => GeneratedScalarKind.Guid,
            _ => default,
        };
        return kind != default || type.SpecialType == SpecialType.System_Boolean;
    }

    private static bool IsValueObject(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Class or TypeKind.Struct };

    private static string? ArrayContainsMethod(ITypeSymbol propertyType, GeneratedScalarKind scalarKind) =>
        propertyType is IArrayTypeSymbol array && !IsNullable(array.ElementType)
            ? scalarKind switch
            {
                GeneratedScalarKind.Boolean => "ContainsBoolean",
                GeneratedScalarKind.String => "ContainsString",
                GeneratedScalarKind.Guid => "ContainsGuid",
                GeneratedScalarKind.Number => NumberArrayContainsMethod(array.ElementType.SpecialType),
                _ => null,
            }
            : null;

    private static string? NumberArrayContainsMethod(SpecialType type) =>
        type switch
        {
            SpecialType.System_Byte => "ContainsByte",
            SpecialType.System_SByte => "ContainsSByte",
            SpecialType.System_Int16 => "ContainsInt16",
            SpecialType.System_UInt16 => "ContainsUInt16",
            SpecialType.System_Int32 => "ContainsInt32",
            SpecialType.System_UInt32 => "ContainsUInt32",
            SpecialType.System_Int64 => "ContainsInt64",
            SpecialType.System_UInt64 => "ContainsUInt64",
            SpecialType.System_Single => "ContainsSingle",
            SpecialType.System_Double => "ContainsDouble",
            SpecialType.System_Decimal => "ContainsDecimal",
            _ => null,
        };
}

using System.Collections;
using System.Reflection;

namespace SiftQL.Schema;

internal static class FilterSchemaCollectionFieldBuilder
{
    public static bool TryAddObjectCollectionFields(
        List<FilterField> fields,
        string path,
        Type collectionType,
        int depth,
        Func<Type, bool> isValueObject)
    {
        Type? elementType = GetElementType(collectionType);
        if (elementType is null ||
            FilterSchemaFallbackBuilder.IsScalar(elementType) ||
            !isValueObject(elementType))
        {
            return false;
        }

        if (!ContainsField(fields, path))
            fields.Add(BuildArrayField(path, elementType));
        AddProperties(fields, path, elementType, depth + 1, isValueObject);
        return true;
    }

    private static void AddProperties(
        List<FilterField> fields,
        string prefix,
        Type ownerType,
        int depth,
        Func<Type, bool> isValueObject)
    {
        if (depth > 3)
            return;

        foreach (PropertyInfo property in FilterSchemaFallbackBuilder.EnumeratePublicProperties(ownerType))
        {
            if (property.GetMethod is null || property.GetMethod.GetParameters().Length != 0)
                continue;

            string name = prefix + "." + property.Name;
            if (ContainsField(fields, name))
                continue;

            Type propertyType = property.PropertyType;
            Type scalarType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (FilterSchemaFallbackBuilder.IsScalar(scalarType))
            {
                fields.Add(BuildArrayField(name, scalarType));
                continue;
            }

            Type? elementType = GetElementType(propertyType);
            if (elementType is not null)
            {
                if (FilterSchemaFallbackBuilder.IsScalar(elementType))
                {
                    fields.Add(BuildArrayField(name, elementType));
                    continue;
                }

                if (isValueObject(elementType))
                    AddProperties(fields, name, elementType, depth + 1, isValueObject);

                continue;
            }

            if (isValueObject(scalarType))
                AddProperties(fields, name, scalarType, depth + 1, isValueObject);
        }
    }

    private static FilterField BuildArrayField(string name, Type valueType) =>
        new(
            name,
            valueType,
            FilterFieldKind.Array,
            subject => FilterCollectionFieldValues.Read(subject, name),
            IsCollectionDerived: true);

    private static Type? GetElementType(Type type)
    {
        if (type == typeof(string))
            return null;

        Type? elementType = type.IsArray
            ? type.GetElementType()
            : type.GetInterfaces()
                .Concat([type])
                .Where(static item => item.IsGenericType)
                .FirstOrDefault(static item => item.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];

        return elementType is null ? null : Nullable.GetUnderlyingType(elementType) ?? elementType;
    }

    private static bool ContainsField(IReadOnlyList<FilterField> fields, string name)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

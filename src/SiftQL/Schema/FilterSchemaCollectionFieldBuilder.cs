using System.Collections;
using System.Reflection;

namespace SiftQL.Schema;

internal static class FilterSchemaCollectionFieldBuilder
{
    public static bool TryAddObjectCollectionFields(
        List<FilterField> fields,
        string path,
        PropertyInfo property,
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
            fields.Add(BuildArrayField(path, elementType, [property]));
        AddProperties(fields, path, elementType, [property], depth + 1, isValueObject);
        return true;
    }

    public static void AddRegisteredFieldsUnderGeneratedCollection(
        List<FilterField> fields,
        string path,
        Type elementType,
        int depth,
        Func<Type, bool> isValueObject)
    {
        if (FilterSchemaFallbackBuilder.IsScalar(elementType))
            return;

        AddProperties(fields, path, elementType, memberPath: null, depth, isValueObject);
    }

    private static void AddProperties(
        List<FilterField> fields,
        string prefix,
        Type ownerType,
        MemberInfo[]? memberPath,
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
                fields.Add(BuildArrayField(name, scalarType, AppendMember(memberPath, property)));
                continue;
            }

            Type? elementType = GetElementType(propertyType);
            if (elementType is not null)
            {
                if (FilterSchemaFallbackBuilder.IsScalar(elementType))
                {
                    fields.Add(BuildArrayField(name, elementType, AppendMember(memberPath, property)));
                    continue;
                }

                if (isValueObject(elementType))
                    AddProperties(
                        fields,
                        name,
                        elementType,
                        AppendMember(memberPath, property),
                        depth + 1,
                        isValueObject);

                continue;
            }

            if (isValueObject(scalarType))
                AddProperties(
                    fields,
                    name,
                    scalarType,
                    AppendMember(memberPath, property),
                    depth + 1,
                    isValueObject);
        }
    }

    private static MemberInfo[]? AppendMember(MemberInfo[]? memberPath, MemberInfo member) =>
        memberPath is null ? null : [.. memberPath, member];

    private static FilterField BuildArrayField(string name, Type valueType, MemberInfo[]? memberPath) =>
        new(
            name,
            valueType,
            FilterFieldKind.Array,
            subject => memberPath is null
                ? FilterCollectionFieldValues.Read(subject, name)
                : FilterCollectionFieldValues.Read(subject, name, memberPath),
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

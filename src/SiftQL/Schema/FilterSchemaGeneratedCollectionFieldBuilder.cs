using System.Collections;
using System.Reflection;
using SiftQL.Translation;

namespace SiftQL.Schema;

internal static class FilterSchemaGeneratedCollectionFieldBuilder
{
    public static void AddRegisteredFields(
        List<FilterField> fields,
        string path,
        Func<object, object?> collectionGetter,
        Type elementType,
        int depth,
        Func<Type, bool> isValueObject)
    {
        if (FilterSchemaFallbackBuilder.IsScalar(elementType))
            return;

        AddProperties(fields, path, string.Empty, elementType, collectionGetter, [], depth, isValueObject);
        AddSubtypeProperties(fields, path, string.Empty, elementType, collectionGetter, [], depth, isValueObject);
    }

    private static void AddProperties(
        List<FilterField> fields,
        string prefix,
        string readPrefix,
        Type ownerType,
        Func<object, object?> collectionGetter,
        MemberInfo[] memberPath,
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

            string readName = AppendPath(readPrefix, property.Name);
            MemberInfo[] nextPath = [.. memberPath, property];
            Type propertyType = property.PropertyType;
            Type scalarType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (FilterSchemaFallbackBuilder.IsScalar(scalarType))
            {
                fields.Add(BuildArrayField(name, scalarType, collectionGetter, readName, nextPath));
                continue;
            }

            Type? elementType = GetElementType(propertyType);
            if (elementType is not null)
            {
                if (FilterSchemaFallbackBuilder.IsScalar(elementType))
                {
                    fields.Add(BuildArrayField(name, elementType, collectionGetter, readName, nextPath));
                    continue;
                }

                if (isValueObject(elementType))
                    AddProperties(fields, name, readName, elementType, collectionGetter, nextPath, depth + 1, isValueObject);

                continue;
            }

            if (isValueObject(scalarType))
                AddProperties(fields, name, readName, scalarType, collectionGetter, nextPath, depth + 1, isValueObject);
        }
    }

    private static void AddSubtypeProperties(
        List<FilterField> fields,
        string fieldPrefix,
        string readPrefix,
        Type baseType,
        Func<object, object?> collectionGetter,
        MemberInfo[] memberPath,
        int depth,
        Func<Type, bool> isValueObject)
    {
        if (depth > 3)
            return;

        foreach (Type subtype in FilterSchema.RegisteredValueObjectSubtypes(baseType))
        {
            string subtypePrefix = fieldPrefix + "." + SubtypeProjection.Segment(subtype);
            AddSubtypeDeclaredProperties(
                fields,
                subtypePrefix,
                readPrefix,
                baseType,
                subtype,
                collectionGetter,
                memberPath,
                depth,
                isValueObject);
        }
    }

    private static void AddSubtypeDeclaredProperties(
        List<FilterField> fields,
        string fieldPrefix,
        string readPrefix,
        Type? baseType,
        Type ownerType,
        Func<object, object?> collectionGetter,
        MemberInfo[] memberPath,
        int depth,
        Func<Type, bool> isValueObject)
    {
        if (depth > 3)
            return;

        foreach (PropertyInfo property in FilterSchemaFallbackBuilder.EnumeratePublicProperties(ownerType))
        {
            if (property.GetMethod is null ||
                property.GetMethod.GetParameters().Length != 0 ||
                baseType is not null && IsReachableFromBase(baseType, property))
            {
                continue;
            }

            string fieldName = fieldPrefix + "." + property.Name;
            if (ContainsField(fields, fieldName))
                continue;

            string readName = AppendPath(readPrefix, property.Name);
            MemberInfo[] nextPath = [.. memberPath, property];
            Type propertyType = property.PropertyType;
            Type scalarType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (FilterSchemaFallbackBuilder.IsScalar(scalarType))
            {
                fields.Add(BuildArrayField(fieldName, scalarType, collectionGetter, readName, nextPath));
                continue;
            }

            Type? elementType = GetElementType(propertyType);
            if (elementType is not null)
            {
                if (FilterSchemaFallbackBuilder.IsScalar(elementType))
                    fields.Add(BuildArrayField(fieldName, elementType, collectionGetter, readName, nextPath));
                continue;
            }

            if (isValueObject(scalarType))
                AddSubtypeDeclaredProperties(
                    fields,
                    fieldName,
                    readName,
                    null,
                    scalarType,
                    collectionGetter,
                    nextPath,
                    depth + 1,
                    isValueObject);
        }
    }

    private static bool IsReachableFromBase(Type baseType, PropertyInfo property) =>
        property.DeclaringType is { } declaringType &&
        declaringType.IsAssignableFrom(baseType);

    private static FilterField BuildArrayField(
        string name,
        Type valueType,
        Func<object, object?> collectionGetter,
        string readPath,
        MemberInfo[] memberPath) =>
        new(
            name,
            valueType,
            FilterFieldKind.Array,
            subject => FilterCollectionFieldValues.Read(collectionGetter(subject), readPath, memberPath),
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

    private static string AppendPath(string prefix, string name) =>
        string.IsNullOrEmpty(prefix) ? name : prefix + "." + name;
}

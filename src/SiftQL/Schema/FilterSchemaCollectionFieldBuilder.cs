using System.Collections;
using System.Reflection;
using SiftQL.Translation;

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
        AddSubtypeProperties(fields, path, path, elementType, [property], depth + 1, isValueObject);
        return true;
    }

    public static void AddRegisteredFieldsUnderGeneratedCollection(
        List<FilterField> fields,
        string path,
        Type subjectType,
        Type elementType,
        int depth,
        Func<Type, bool> isValueObject)
    {
        if (FilterSchemaFallbackBuilder.IsScalar(elementType))
            return;

        AddProperties(
            fields,
            path,
            elementType,
            ResolveMemberPath(subjectType, path),
            depth,
            isValueObject);
        AddSubtypeProperties(
            fields,
            path,
            path,
            elementType,
            ResolveMemberPath(subjectType, path),
            depth,
            isValueObject);
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

    private static void AddSubtypeProperties(
        List<FilterField> fields,
        string fieldPrefix,
        string readPrefix,
        Type baseType,
        MemberInfo[]? memberPath,
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
        MemberInfo[]? memberPath,
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

            string readName = readPrefix + "." + property.Name;
            MemberInfo[]? nextPath = AppendMember(memberPath, property);
            Type propertyType = property.PropertyType;
            Type scalarType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (FilterSchemaFallbackBuilder.IsScalar(scalarType))
            {
                fields.Add(BuildArrayField(fieldName, scalarType, nextPath, readName));
                continue;
            }

            Type? elementType = GetElementType(propertyType);
            if (elementType is not null)
            {
                if (FilterSchemaFallbackBuilder.IsScalar(elementType))
                    fields.Add(BuildArrayField(fieldName, elementType, nextPath, readName));
                continue;
            }

            if (isValueObject(scalarType))
                AddSubtypeDeclaredProperties(
                    fields,
                    fieldName,
                    readName,
                    null,
                    scalarType,
                    nextPath,
                    depth + 1,
                    isValueObject);
        }
    }

    private static bool IsReachableFromBase(Type baseType, PropertyInfo property) =>
        property.DeclaringType is { } declaringType &&
        declaringType.IsAssignableFrom(baseType);

    private static MemberInfo[]? AppendMember(MemberInfo[]? memberPath, MemberInfo member) =>
        memberPath is null ? null : [.. memberPath, member];

    private static MemberInfo[]? ResolveMemberPath(Type subjectType, string path)
    {
        string[] segments = path.Split('.');
        var members = new MemberInfo[segments.Length];
        Type ownerType = subjectType;
        for (int i = 0; i < segments.Length; i++)
        {
            PropertyInfo? property = FindProperty(ownerType, segments[i]);
            if (property is null)
                return null;

            members[i] = property;
            ownerType = NextOwnerType(property.PropertyType);
        }

        return members;
    }

    private static PropertyInfo? FindProperty(Type ownerType, string name)
    {
        foreach (PropertyInfo property in FilterSchemaFallbackBuilder.EnumeratePublicProperties(ownerType))
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.GetMethod is not null &&
                property.GetMethod.GetParameters().Length == 0)
            {
                return property;
            }
        }

        return null;
    }

    private static Type NextOwnerType(Type memberType)
    {
        Type type = Nullable.GetUnderlyingType(memberType) ?? memberType;
        return GetElementType(type) ?? type;
    }

    private static FilterField BuildArrayField(
        string name,
        Type valueType,
        MemberInfo[]? memberPath,
        string? readPath = null) =>
        new(
            name,
            valueType,
            FilterFieldKind.Array,
            subject => memberPath is null
                ? FilterCollectionFieldValues.Read(subject, readPath ?? name)
                : FilterCollectionFieldValues.Read(subject, readPath ?? name, memberPath),
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

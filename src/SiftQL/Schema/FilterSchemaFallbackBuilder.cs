using System.Linq.Expressions;
using System.Reflection;
using SiftQL.Compiler;

namespace SiftQL.Schema;

internal static class FilterSchemaFallbackBuilder
{
    public static FilterSchema Build(
        Type subjectType,
        Func<Type, bool> isValueObject,
        NullabilityInfoContext nullability)
    {
        var fields = new List<FilterField>
        {
            BuildVirtualField(subjectType, "subjectType", static type => type.FullName ?? type.Name),
            BuildVirtualField(subjectType, "subjectName", static type => type.Name),
        };

        var parameter = Expression.Parameter(typeof(object), "subject");
        var typedSubject = Expression.Convert(parameter, subjectType);
        AddProperties(
            fields,
            string.Empty,
            subjectType,
            typedSubject,
            typedSubject,
            parameter,
            depth: 0,
            isValueObject,
            nullability);
        return new FilterSchema(subjectType, fields);
    }

    public static void AddRegisteredFieldsUnderGeneratedObjects(
        Type subjectType,
        IEnumerable<FilterField> generatedFields,
        List<FilterField> fields,
        Func<Type, bool> isValueObject,
        NullabilityInfoContext nullability)
    {
        var parameter = Expression.Parameter(typeof(object), "subject");
        var typedSubject = Expression.Convert(parameter, subjectType);
        foreach (FilterField field in generatedFields)
        {
            if (field.Kind != FilterFieldKind.Object ||
                field.Access?.PropertyPath is not { } path)
            {
                continue;
            }

            Expression? ownerExpression = FilterFieldAccessExpression.Build(typedSubject, path);
            if (ownerExpression is null ||
                IsNullableExpression(ownerExpression) ||
                PathCanReturnNull(subjectType, path, nullability))
            {
                continue;
            }

            AddProperties(
                fields,
                field.Name,
                field.ValueType,
                ownerExpression,
                typedSubject,
                parameter,
                Depth(field.Name),
                isValueObject,
                nullability);
        }
    }

    private static bool PathCanReturnNull(Type ownerType, string path, NullabilityInfoContext nullability)
    {
        Type current = ownerType;
        foreach (string segment in path.Split('.'))
        {
            PropertyInfo? property = FindProperty(current, segment);
            if (property is null || IsNullableProperty(property, nullability))
                return true;

            current = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }

        return false;
    }

    private static PropertyInfo? FindProperty(Type ownerType, string name)
    {
        foreach (PropertyInfo property in EnumeratePublicProperties(ownerType))
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

    private static void AddProperties(
        List<FilterField> fields,
        string prefix,
        Type ownerType,
        Expression ownerExpression,
        Expression rootExpression,
        ParameterExpression parameter,
        int depth,
        Func<Type, bool> isValueObject,
        NullabilityInfoContext nullability)
    {
        if (depth > 3)
            return;

        foreach (PropertyInfo property in EnumeratePublicProperties(ownerType))
        {
            if (property.GetMethod is null || property.GetMethod.GetParameters().Length != 0)
                continue;

            string name = string.IsNullOrEmpty(prefix) ? property.Name : prefix + "." + property.Name;
            if (string.IsNullOrEmpty(prefix) && IsReservedMetadataField(name))
                throw ReservedMetadataCollision(ownerType, property.Name);
            if (ContainsField(fields, name))
                continue;

            Type propertyType = property.PropertyType;
            Expression propertyExpression = Expression.Property(
                ConvertToDeclaringType(ownerExpression, property.DeclaringType),
                property);
            Type scalarType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (IsScalar(scalarType))
            {
                fields.Add(BuildField(
                    name,
                    scalarType,
                    FilterFieldKind.Scalar,
                    propertyExpression,
                    rootExpression,
                    parameter));
                continue;
            }

            Type? elementType = GetScalarElementType(propertyType);
            if (elementType is not null)
            {
                fields.Add(BuildField(
                    name,
                    elementType,
                    FilterFieldKind.Array,
                    propertyExpression,
                    rootExpression,
                    parameter));
                continue;
            }

            if (isValueObject(scalarType))
            {
                fields.Add(BuildField(
                    name,
                    scalarType,
                    FilterFieldKind.Object,
                    propertyExpression,
                    rootExpression,
                    parameter));
                if (!IsNullableProperty(property, nullability))
                {
                    AddProperties(
                        fields,
                        name,
                        scalarType,
                        propertyExpression,
                        rootExpression,
                        parameter,
                        depth + 1,
                        isValueObject,
                        nullability);
                }
            }
        }
    }

    private static IEnumerable<PropertyInfo> EnumeratePublicProperties(Type ownerType)
    {
        foreach (PropertyInfo property in ownerType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            yield return property;

        if (!ownerType.IsInterface)
            yield break;

        foreach (Type inherited in ownerType.GetInterfaces())
        {
            foreach (PropertyInfo property in inherited.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                yield return property;
        }
    }

    private static Expression ConvertToDeclaringType(Expression expression, Type? declaringType) =>
        declaringType is not null &&
        declaringType != expression.Type &&
        declaringType.IsAssignableFrom(expression.Type)
            ? Expression.Convert(expression, declaringType)
            : expression;

    private static bool IsNullableProperty(
        PropertyInfo property,
        NullabilityInfoContext nullability)
    {
        Type propertyType = property.PropertyType;
        if (Nullable.GetUnderlyingType(propertyType) is not null)
            return true;

        return !propertyType.IsValueType &&
            nullability.Create(property).ReadState == NullabilityState.Nullable;
    }

    private static bool IsNullableExpression(Expression expression) =>
        Nullable.GetUnderlyingType(expression.Type) is not null;

    private static bool ContainsField(IReadOnlyList<FilterField> fields, string name)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsReservedMetadataField(string name) =>
        string.Equals(name, "subjectType", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "subjectName", StringComparison.OrdinalIgnoreCase);

    private static FilterValidationException ReservedMetadataCollision(
        Type subjectType,
        string propertyName) =>
        new(
            $"Filter subject '{subjectType.FullName}' property '{propertyName}' collides with reserved metadata field '{propertyName}'.");

    private static FilterField BuildVirtualField(
        Type subjectType,
        string name,
        Func<Type, string> valueFactory)
    {
        string value = valueFactory(subjectType);
        bool dynamicValue = subjectType.IsInterface || subjectType.IsAbstract;
        return new(
            name,
            typeof(string),
            FilterFieldKind.Scalar,
            subject => dynamicValue ? valueFactory(subject.GetType()) : value,
            ProjectionAccessor: subject => ProjectionValueFactory.FromString(
                dynamicValue ? valueFactory(subject.GetType()) : value),
            Access: dynamicValue ? null : FilterFieldAccess.ForConstant(value));
    }

    private static FilterField BuildField(
        string name,
        Type valueType,
        FilterFieldKind kind,
        Expression propertyExpression,
        Expression rootExpression,
        ParameterExpression parameter)
    {
        Expression accessExpression = name.Contains('.', StringComparison.Ordinal)
            ? FilterFieldAccessExpression.Build(rootExpression, name) ?? propertyExpression
            : propertyExpression;
        Expression boxed = Expression.Convert(accessExpression, typeof(object));
        var getter = Expression.Lambda<Func<object, object?>>(boxed, parameter).Compile();
        var scalarAccessor = kind == FilterFieldKind.Scalar
            ? FilterSchemaAccessors.BuildScalar(valueType, accessExpression, parameter)
            : null;
        var arrayAccessor = kind == FilterFieldKind.Array
            ? FilterSchemaAccessors.BuildArray(valueType, accessExpression, parameter)
            : null;
        var projectionAccessor = kind == FilterFieldKind.Scalar
            ? FilterSchemaAccessors.BuildProjection(valueType, accessExpression, parameter)
            : kind == FilterFieldKind.Object
                ? FilterSchemaAccessors.BuildObjectProjection(accessExpression, parameter)
                : null;
        return new FilterField(
            name,
            valueType,
            kind,
            getter,
            scalarAccessor,
            arrayAccessor,
            projectionAccessor,
            FilterFieldAccess.ForProperty(name));
    }

    private static Type? GetScalarElementType(Type type)
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

        if (elementType is null)
            return null;

        Type scalar = Nullable.GetUnderlyingType(elementType) ?? elementType;
        return IsScalar(scalar) ? scalar : null;
    }

    private static bool IsScalar(Type type) =>
        type.IsEnum ||
        type == typeof(bool) ||
        type == typeof(byte) ||
        type == typeof(sbyte) ||
        type == typeof(short) ||
        type == typeof(ushort) ||
        type == typeof(int) ||
        type == typeof(uint) ||
        type == typeof(long) ||
        type == typeof(ulong) ||
        type == typeof(float) ||
        type == typeof(double) ||
        type == typeof(decimal) ||
        type == typeof(string) ||
        type == typeof(Guid);

    private static int Depth(string name) =>
        name.Count(static item => item == '.') + 1;
}

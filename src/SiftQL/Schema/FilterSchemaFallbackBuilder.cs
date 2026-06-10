using System.Linq.Expressions;
using System.Reflection;
using SiftQL.Compiler;
using SiftQL.Translation;

namespace SiftQL.Schema;

internal static class FilterSchemaFallbackBuilder
{
    public static FilterSchema Build(
        Type subjectType,
        Func<Type, bool> isValueObject)
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
            isValueObject);
        AddSubtypeProjectedFields(fields, string.Empty, subjectType, typedSubject, parameter, isValueObject);
        return new FilterSchema(subjectType, fields);
    }

    public static void AddRegisteredFieldsUnderGeneratedObjects(
        Type subjectType,
        IEnumerable<FilterField> generatedFields,
        List<FilterField> fields,
        Func<Type, bool> isValueObject)
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
                PathBlocksNestedExpansion(subjectType, path))
            {
                continue;
            }

            // Guarded access lifts struct owners to Nullable<T>; unwrap so
            // the owner's properties stay addressable during expansion.
            ownerExpression = UnwrapNullableValue(ownerExpression);
            AddProperties(
                fields,
                field.Name,
                field.ValueType,
                ownerExpression,
                typedSubject,
                parameter,
                Depth(field.Name),
                isValueObject);
        }
    }

    // Nullable reference owners expand fine because nested accessors
    // null-propagate; Nullable<T> owners cannot, because the value object's
    // properties are not addressable through Nullable<T>.
    private static bool PathBlocksNestedExpansion(Type ownerType, string path)
    {
        Type current = ownerType;
        foreach (string segment in path.Split('.'))
        {
            PropertyInfo? property = FindProperty(current, segment);
            if (property is null || Nullable.GetUnderlyingType(property.PropertyType) is not null)
                return true;

            current = property.PropertyType;
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
        Func<Type, bool> isValueObject)
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
            Expression propertyExpression = GuardedPropertyAccess(ownerExpression, property);
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

            if (FilterSchemaCollectionFieldBuilder.TryAddObjectCollectionFields(
                    fields,
                    name,
                    propertyType,
                    depth,
                    isValueObject))
            {
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
                // Nullable reference owners still expand: nested accessors
                // null-propagate. Nullable<T> owners cannot be expanded.
                if (Nullable.GetUnderlyingType(propertyType) is null)
                {
                    AddProperties(
                        fields,
                        name,
                        scalarType,
                        propertyExpression,
                        rootExpression,
                        parameter,
                        depth + 1,
                        isValueObject);
                    AddSubtypeProjectedFields(fields, name, scalarType, propertyExpression, parameter, isValueObject);
                }
            }
        }
    }

    // Projects each registered value-object subtype's subtype-specific members
    // under a `<Subtype>`-qualified path so a downcast read like
    // `(x.Member as Sub).Prop` resolves. Members already reachable through the
    // declared base type stay flat. Reading the projection against an instance of
    // a different subtype yields null (no match), never an error. See
    // [[SubtypeProjection]].
    private static void AddSubtypeProjectedFields(
        List<FilterField> fields,
        string prefix,
        Type baseType,
        Expression ownerExpression,
        ParameterExpression parameter,
        Func<Type, bool> isValueObject)
    {
        foreach (Type subtype in FilterSchema.RegisteredValueObjectSubtypes(baseType))
        {
            string segment = SubtypeProjection.Segment(subtype);
            string subtypePrefix = string.IsNullOrEmpty(prefix) ? segment : prefix + "." + segment;
            foreach (PropertyInfo property in EnumeratePublicProperties(subtype))
            {
                if (property.GetMethod is null ||
                    property.GetMethod.GetParameters().Length != 0 ||
                    IsReachableFromBase(baseType, property))
                {
                    continue;
                }

                string name = subtypePrefix + "." + property.Name;
                if (ContainsField(fields, name))
                    continue;

                Type propertyType = property.PropertyType;
                Type scalarType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
                Expression access = CastGuardedAccess(ownerExpression, subtype, property);
                if (IsScalar(scalarType))
                {
                    fields.Add(BuildSubtypeField(name, scalarType, FilterFieldKind.Scalar, access, parameter));
                    continue;
                }

                Type? elementType = GetScalarElementType(propertyType);
                if (elementType is not null)
                {
                    fields.Add(BuildSubtypeField(name, elementType, FilterFieldKind.Array, access, parameter));
                    continue;
                }

                if (isValueObject(scalarType))
                {
                    fields.Add(BuildSubtypeField(name, scalarType, FilterFieldKind.Object, access, parameter));
                    if (Nullable.GetUnderlyingType(propertyType) is null)
                    {
                        AddProperties(
                            fields,
                            name,
                            scalarType,
                            access,
                            access,
                            parameter,
                            Depth(name),
                            isValueObject);
                    }
                }
            }
        }
    }

    private static bool IsReachableFromBase(Type baseType, PropertyInfo property)
    {
        if (property.DeclaringType is null ||
            property.DeclaringType.IsAssignableFrom(baseType))
        {
            return true;
        }

        MethodInfo? baseDefinition = property.GetMethod?.GetBaseDefinition();
        return baseDefinition?.DeclaringType is { } declaringType &&
            declaringType != property.DeclaringType &&
            declaringType.IsAssignableFrom(baseType);
    }

    // Null-safe `(owner as subtype)?.property`: when the runtime value is not the
    // subtype the cast yields null and the field reads as absent. The cast is
    // bound to a local so it is evaluated once, not re-run for the null guard.
    private static Expression CastGuardedAccess(Expression owner, Type subtype, PropertyInfo property)
    {
        ParameterExpression casted = Expression.Variable(subtype, "casted");
        Expression propertyAccess = Expression.Property(casted, property);
        Type resultType = LiftValueType(propertyAccess.Type);
        Expression value = resultType == propertyAccess.Type
            ? propertyAccess
            : Expression.Convert(propertyAccess, resultType);
        return Expression.Block(
            [casted],
            Expression.Assign(casted, Expression.TypeAs(owner, subtype)),
            Expression.Condition(
                Expression.NotEqual(casted, Expression.Constant(null, subtype)),
                value,
                Expression.Default(resultType)));
    }

    private static Expression GuardedPropertyAccess(Expression owner, PropertyInfo property)
    {
        Expression convertedOwner = ConvertToDeclaringType(owner, property.DeclaringType);
        if (!CanBeNull(convertedOwner.Type))
            return Expression.Property(convertedOwner, property);

        ParameterExpression local = Expression.Variable(convertedOwner.Type, "owner");
        Expression propertyAccess = Expression.Property(local, property);
        Type resultType = LiftValueType(propertyAccess.Type);
        Expression value = resultType == propertyAccess.Type
            ? propertyAccess
            : Expression.Convert(propertyAccess, resultType);
        return Expression.Block(
            [local],
            Expression.Assign(local, convertedOwner),
            Expression.Condition(
                IsNull(local),
                Expression.Default(resultType),
                value));
    }

    private static FilterField BuildSubtypeField(
        string name,
        Type valueType,
        FilterFieldKind kind,
        Expression accessExpression,
        ParameterExpression parameter)
    {
        Expression boxed = Expression.Convert(accessExpression, typeof(object));
        var getter = Expression.Lambda<Func<object, object?>>(boxed, parameter).Compile();
        return new FilterField(
            name,
            valueType,
            kind,
            getter,
            kind == FilterFieldKind.Scalar
                ? FilterSchemaAccessors.BuildScalar(valueType, accessExpression, parameter)
                : null,
            kind == FilterFieldKind.Array
                ? FilterSchemaAccessors.BuildArray(valueType, accessExpression, parameter)
                : null,
            kind == FilterFieldKind.Scalar
                ? FilterSchemaAccessors.BuildProjection(valueType, accessExpression, parameter)
                : null);
    }

    private static Type LiftValueType(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null
            ? typeof(Nullable<>).MakeGenericType(type)
            : type;

    private static bool CanBeNull(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static Expression IsNull(Expression expression)
    {
        if (Nullable.GetUnderlyingType(expression.Type) is not null)
            return Expression.Not(Expression.Property(expression, nameof(Nullable<int>.HasValue)));

        return Expression.Equal(expression, Expression.Constant(null, expression.Type));
    }

    internal static IEnumerable<PropertyInfo> EnumeratePublicProperties(Type ownerType)
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

    private static Expression UnwrapNullableValue(Expression expression) =>
        Nullable.GetUnderlyingType(expression.Type) is not null
            ? Expression.Property(expression, nameof(Nullable<int>.Value))
            : expression;

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
        string.Equals(name, "subjectName", StringComparison.OrdinalIgnoreCase) ||
        SubjectTypeMetadata.IsReservedName(name);

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
        bool dynamicValue = !subjectType.IsSealed;
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

    internal static bool IsScalar(Type type) =>
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
        type == typeof(Guid) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(DateTime) ||
        type == typeof(DateOnly);

    private static int Depth(string name) =>
        name.Count(static item => item == '.') + 1;
}

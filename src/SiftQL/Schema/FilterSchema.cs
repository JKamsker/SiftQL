using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace SiftQL.Schema;

public sealed class FilterSchema
{
    private static readonly ConcurrentDictionary<Type, FilterSchema> s_cache = new();
    private static readonly ConcurrentDictionary<Assembly, GeneratedFilterSchemaProviderDelegate> s_generatedProviders = new();
    private static readonly HashSet<Type> s_valueObjects = [];

    public static void RegisterValueObject<T>() => s_valueObjects.Add(typeof(T));

    public static void RegisterValueObject(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        s_valueObjects.Add(type);
    }

    private readonly Dictionary<string, FilterField> _fields;

    internal FilterSchema(Type subjectType, IReadOnlyList<FilterField> fields)
    {
        SubjectType = subjectType;
        _fields = fields.ToDictionary(static field => field.Name, StringComparer.OrdinalIgnoreCase);
    }

    public Type SubjectType { get; }
    public IReadOnlyCollection<string> FieldNames => _fields.Keys;

    public static FilterSchema For(Type subjectType)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        return s_cache.GetOrAdd(subjectType, Build);
    }

    internal static FilterSchema BuildUncachedForBenchmarks(Type subjectType)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        return Build(subjectType);
    }

    internal static void RegisterGeneratedProvider(
        Assembly assembly,
        GeneratedFilterSchemaProviderDelegate provider) =>
        s_generatedProviders[assembly] = provider;

    public bool TryGetField(string name, out FilterField field) =>
        _fields.TryGetValue(name, out field!);

    private static FilterSchema Build(Type subjectType)
    {
        if (GeneratedFilterSchemaProvider.TryCreate(subjectType, out var schema))
            return schema!;

        return TryCreateRegistered(subjectType, out schema)
            ? schema!
            : BuildFallback(subjectType);
    }

    private static bool TryCreateRegistered(Type subjectType, out FilterSchema? schema)
    {
        if (s_generatedProviders.TryGetValue(subjectType.Assembly, out var provider) &&
            provider(subjectType, out schema))
        {
            return true;
        }

        schema = null;
        return false;
    }

    private static FilterSchema BuildFallback(Type subjectType)
    {
        var fields = new List<FilterField>
        {
            BuildVirtualField(subjectType, "subjectType", static type => type.FullName ?? type.Name),
            BuildVirtualField(subjectType, "subjectName", static type => type.Name),
        };

        var parameter = Expression.Parameter(typeof(object), "subject");
        var typedSubject = Expression.Convert(parameter, subjectType);
        AddProperties(fields, string.Empty, subjectType, typedSubject, parameter, depth: 0);
        return new FilterSchema(subjectType, fields);
    }

    private static void AddProperties(
        List<FilterField> fields,
        string prefix,
        Type ownerType,
        Expression ownerExpression,
        ParameterExpression parameter,
        int depth)
    {
        if (depth > 3) return;

        foreach (PropertyInfo property in ownerType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is null || property.GetMethod.GetParameters().Length != 0)
                continue;

            string name = string.IsNullOrEmpty(prefix) ? property.Name : prefix + "." + property.Name;
            if (ContainsField(fields, name))
                continue;

            Type propertyType = property.PropertyType;
            Expression propertyExpression = Expression.Property(ownerExpression, property);
            Type scalarType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (IsScalar(scalarType))
            {
                fields.Add(BuildField(name, scalarType, FilterFieldKind.Scalar, propertyExpression, parameter));
                continue;
            }

            Type? elementType = GetScalarElementType(propertyType);
            if (elementType is not null)
            {
                fields.Add(BuildField(name, elementType, FilterFieldKind.Array, propertyExpression, parameter));
                continue;
            }

            if (s_valueObjects.Contains(scalarType))
            {
                fields.Add(BuildField(name, scalarType, FilterFieldKind.Object, propertyExpression, parameter));
                AddProperties(fields, name, scalarType, propertyExpression, parameter, depth + 1);
            }
        }
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
        ParameterExpression parameter)
    {
        Expression boxed = Expression.Convert(propertyExpression, typeof(object));
        var getter = Expression.Lambda<Func<object, object?>>(boxed, parameter).Compile();
        var scalarAccessor = kind == FilterFieldKind.Scalar
            ? FilterSchemaAccessors.BuildScalar(valueType, propertyExpression, parameter)
            : null;
        var arrayAccessor = kind == FilterFieldKind.Array
            ? FilterSchemaAccessors.BuildArray(valueType, propertyExpression, parameter)
            : null;
        var projectionAccessor = kind == FilterFieldKind.Scalar
            ? FilterSchemaAccessors.BuildProjection(valueType, propertyExpression, parameter)
            : kind == FilterFieldKind.Object
                ? FilterSchemaAccessors.BuildObjectProjection(propertyExpression, parameter)
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
        if (type == typeof(string)) return null;

        Type? elementType = type.IsArray
            ? type.GetElementType()
            : type.GetInterfaces()
                .Concat([type])
                .Where(static item => item.IsGenericType)
                .FirstOrDefault(static item => item.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];

        if (elementType is null) return null;

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

    private static bool IsNumeric(Type type) =>
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
        type == typeof(decimal);
}

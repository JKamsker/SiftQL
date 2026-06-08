using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace SiftQL.Schema;

public sealed class FilterSchema
{
    private static readonly ConcurrentDictionary<SchemaCacheKey, FilterSchema> s_cache = new();
    private static readonly ConcurrentDictionary<Assembly, GeneratedFilterSchemaProviderDelegate> s_generatedProviders = new();
    private static readonly ConcurrentDictionary<Type, byte> s_valueObjects = new();
    private static readonly NullabilityInfoContext s_nullability = new();
    private static int s_valueObjectVersion;
    private static int s_schemaVersion;

    public static void RegisterValueObject<T>() => RegisterValueObject(typeof(T));

    public static void RegisterValueObject(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!s_valueObjects.TryAdd(type, 0))
            return;

        Interlocked.Increment(ref s_valueObjectVersion);
        IncrementSchemaVersion();
        s_cache.Clear();
    }

    private readonly Dictionary<string, FilterField> _fields;

    internal FilterSchema(Type subjectType, IReadOnlyList<FilterField> fields)
    {
        SubjectType = subjectType;
        _fields = fields.ToDictionary(static field => field.Name, StringComparer.OrdinalIgnoreCase);
    }

    public Type SubjectType { get; }
    public IReadOnlyCollection<string> FieldNames => _fields.Keys;
    internal static int Version => Volatile.Read(ref s_schemaVersion);

    public static FilterSchema For(Type subjectType)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        var key = new SchemaCacheKey(subjectType, Version);
        return s_cache.GetOrAdd(key, static item => Build(item.SubjectType));
    }

    internal static FilterSchema BuildUncachedForBenchmarks(Type subjectType)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        return Build(subjectType);
    }

    internal static void RegisterGeneratedProvider(
        Assembly assembly,
        GeneratedFilterSchemaProviderDelegate provider)
    {
        s_generatedProviders[assembly] = provider;
        IncrementSchemaVersion();
        s_cache.Clear();
    }

    public bool TryGetField(string name, out FilterField field) =>
        _fields.TryGetValue(name, out field!);

    private static FilterSchema Build(Type subjectType)
    {
        if (GeneratedFilterSchemaProvider.TryCreate(subjectType, out var schema))
            return MergeRegisteredValueObjectFields(schema!);

        return TryCreateRegistered(subjectType, out schema)
            ? MergeRegisteredValueObjectFields(schema!)
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

    private static FilterSchema MergeRegisteredValueObjectFields(FilterSchema generated)
    {
        if (Volatile.Read(ref s_valueObjectVersion) == 0)
            return generated;

        FilterSchema fallback = BuildFallback(generated.SubjectType);
        var fields = new List<FilterField>(generated._fields.Values);
        foreach (FilterField field in fallback._fields.Values)
        {
            if (!generated._fields.ContainsKey(field.Name))
                fields.Add(field);
        }

        return new FilterSchema(generated.SubjectType, fields);
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
        AddProperties(fields, string.Empty, subjectType, typedSubject, typedSubject, parameter, depth: 0);
        return new FilterSchema(subjectType, fields);
    }

    private static void AddProperties(
        List<FilterField> fields,
        string prefix,
        Type ownerType,
        Expression ownerExpression,
        Expression rootExpression,
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

            if (s_valueObjects.ContainsKey(scalarType))
            {
                fields.Add(BuildField(
                    name,
                    scalarType,
                    FilterFieldKind.Object,
                    propertyExpression,
                    rootExpression,
                    parameter));
                if (!IsNullableProperty(property))
                    AddProperties(fields, name, scalarType, propertyExpression, rootExpression, parameter, depth + 1);
            }
        }
    }

    private static bool IsNullableProperty(PropertyInfo property)
    {
        Type propertyType = property.PropertyType;
        if (Nullable.GetUnderlyingType(propertyType) is not null)
            return true;

        return !propertyType.IsValueType &&
            s_nullability.Create(property).ReadState == NullabilityState.Nullable;
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

    private static void IncrementSchemaVersion() =>
        Interlocked.Increment(ref s_schemaVersion);

    private readonly record struct SchemaCacheKey(Type SubjectType, int SchemaVersion);
}

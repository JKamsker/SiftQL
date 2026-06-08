using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL.Compiler;

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
            return MergeRegisteredValueObjectFields(ValidateProviderSchema(subjectType, schema));

        return TryCreateRegistered(subjectType, out schema)
            ? MergeRegisteredValueObjectFields(schema!)
            : BuildFallback(subjectType);
    }

    private static bool TryCreateRegistered(Type subjectType, out FilterSchema? schema)
    {
        if (TryCreateRegisteredCore(subjectType, out schema))
            return true;

        RuntimeHelpers.RunModuleConstructor(subjectType.Assembly.ManifestModule.ModuleHandle);
        return TryCreateRegisteredCore(subjectType, out schema);
    }

    private static bool TryCreateRegisteredCore(Type subjectType, out FilterSchema? schema)
    {
        if (s_generatedProviders.TryGetValue(subjectType.Assembly, out var provider) &&
            provider(subjectType, out schema))
        {
            schema = ValidateProviderSchema(subjectType, schema);
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

        FilterSchemaFallbackBuilder.AddRegisteredFieldsUnderGeneratedObjects(
            generated.SubjectType,
            generated._fields.Values,
            fields,
            IsRegisteredValueObject,
            s_nullability);
        return new FilterSchema(generated.SubjectType, fields);
    }

    private static FilterSchema BuildFallback(Type subjectType) =>
        FilterSchemaFallbackBuilder.Build(subjectType, IsRegisteredValueObject, s_nullability);

    private static FilterSchema ValidateProviderSchema(
        Type requestedType,
        FilterSchema? schema)
    {
        if (schema is not null && schema.SubjectType == requestedType)
            return schema;

        string actual = schema?.SubjectType.FullName ?? "<null>";
        throw new FilterValidationException(
            $"Generated filter schema provider for '{requestedType.FullName}' returned schema for '{actual}'.");
    }

    private static bool IsRegisteredValueObject(Type type) =>
        s_valueObjects.ContainsKey(type);

    private static void IncrementSchemaVersion() =>
        Interlocked.Increment(ref s_schemaVersion);

    private readonly record struct SchemaCacheKey(Type SubjectType, int SchemaVersion);
}

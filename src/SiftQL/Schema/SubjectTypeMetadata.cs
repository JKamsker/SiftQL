using System.Collections.Concurrent;
using SiftQL.Projected;

namespace SiftQL.Schema;

// Backs the C# `is` operator in filter predicates. Every filter subject (and
// every object-typed member) exposes a synthetic reserved `subjectTypes` array
// field holding the full type-name ancestry of the runtime value (its own type,
// its base types up to but excluding System.Object, and all implemented
// interfaces). `x is T` translates to Contains("<path>.subjectTypes",
// typeof(T).FullName); because the array carries the whole ancestry, the test
// matches T and every subtype/interface implementation of T, mirroring the CLR
// `is` operator instead of only the leaf type.
internal static class SubjectTypeMetadata
{
    public const string FieldName = "subjectTypes";

    private static readonly string s_nestedSuffix = "." + FieldName;
    private static readonly ConcurrentDictionary<Type, string[]> s_ancestry = new();

    public static bool IsReservedName(string name) =>
        string.Equals(name, FieldName, StringComparison.OrdinalIgnoreCase);

    // True for the root discriminator and any nested `<member>.subjectTypes`
    // path, so the synthetic fields are treated as virtual metadata (e.g.
    // excluded from default projection) like subjectType/subjectName.
    public static bool IsDiscriminatorPath(string name) =>
        IsReservedName(name) ||
        name.EndsWith(s_nestedSuffix, StringComparison.OrdinalIgnoreCase);

    public static string[] Of(object? value) =>
        value is null ? [] : Of(value.GetType());

    // Ancestry is immutable per type and read on every Matches(); cache it. The
    // returned array is shared and treated as read-only by all callers.
    public static string[] Of(Type type) =>
        s_ancestry.GetOrAdd(type, ComputeAncestry);

    private static string[] ComputeAncestry(Type type)
    {
        var names = new List<string>();
        for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
            AddName(names, current);
        foreach (Type contract in type.GetInterfaces())
            AddName(names, contract);
        return names.ToArray();
    }

    private static void AddName(List<string> names, Type type)
    {
        string name = type.FullName ?? type.Name;
        if (!names.Contains(name))
            names.Add(name);
    }

    // Adds the reserved `subjectTypes` field for the subject itself plus a
    // `<member>.subjectTypes` field for every object-typed member already in the
    // schema. Idempotent (skips names already present) so it is safe to run on
    // every FilterSchema construction, including the merge of registered value
    // objects into a generated schema.
    public static void Augment(
        Type subjectType,
        IReadOnlyList<FilterField> fields,
        Dictionary<string, FilterField> map)
    {
        // Projected events keep only EventType/EventName strings after
        // projection, so the runtime ancestry is unavailable; they expose type
        // identity through the existing subjectType/subjectName fields instead.
        if (subjectType == typeof(ProjectedEvent))
            return;

        if (!map.ContainsKey(FieldName))
            map[FieldName] = Field(FieldName, static subject => Of(subject));

        for (int i = 0; i < fields.Count; i++)
        {
            FilterField field = fields[i];
            if (field.Kind != FilterFieldKind.Object)
                continue;

            string name = field.Name + "." + FieldName;
            if (map.ContainsKey(name))
                continue;

            Func<object, object?> objectGetter = field.Getter;
            map[name] = Field(name, subject => Of(objectGetter(subject)));
        }
    }

    private static FilterField Field(string name, Func<object, object?> getter) =>
        new(
            name,
            typeof(string),
            FilterFieldKind.Array,
            getter,
            ScalarAccessor: null,
            ArrayAccessor: new FilterArrayAccessor(
                FilterScalarKind.String,
                textContains: (subject, expected) => Contains(getter(subject), expected)),
            ProjectionAccessor: null,
            Access: null,
            IsCollectionDerived: false);

    private static bool Contains(object? value, string? expected) =>
        expected is not null && value is string[] names && Array.IndexOf(names, expected) >= 0;
}

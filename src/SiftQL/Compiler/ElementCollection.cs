using System.Collections;
using System.Reflection;

namespace SiftQL.Compiler;

// Resolves a collection field path on a subject type to a raw element enumerable
// and its element type, via reflection. Used by ElemMatch, which evaluates a
// child filter against each element and so needs the elements themselves rather
// than the flattened per-member projections the schema exposes.
internal static class ElementCollection
{
    public static bool TryResolve(
        Type subjectType,
        string path,
        out Func<object, IEnumerable?> getter,
        out Type elementType)
    {
        getter = static _ => null;
        elementType = typeof(object);

        var chain = new List<PropertyInfo>();
        Type current = subjectType;
        foreach (string segment in path.Split('.'))
        {
            PropertyInfo? property = FindProperty(current, segment);
            if (property is null)
                return false;
            chain.Add(property);
            current = property.PropertyType;
        }

        Type? element = GetElementType(current);
        if (element is null)
            return false;

        elementType = element;
        PropertyInfo[] properties = chain.ToArray();
        getter = subject =>
        {
            object? value = subject;
            foreach (PropertyInfo property in properties)
            {
                if (value is null)
                    return null;
                // Guard the runtime type so an unexpected subject (e.g. a
                // polymorphic mismatch) yields a safe non-match instead of a
                // TargetException from reflection.
                if (property.DeclaringType is { } declaring && !declaring.IsInstanceOfType(value))
                    return null;
                value = property.GetValue(value);
            }

            return value as IEnumerable;
        };
        return true;
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.GetMethod is not null &&
                property.GetIndexParameters().Length == 0)
            {
                return property;
            }
        }

        return null;
    }

    private static Type? GetElementType(Type type)
    {
        if (type == typeof(string))
            return null;
        if (type.IsArray)
            return type.GetElementType();

        Type? enumerable = type.GetInterfaces()
            .Concat([type])
            .Where(static item => item.IsGenericType)
            .FirstOrDefault(static item => item.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0];
    }
}

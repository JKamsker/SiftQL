using System.Collections;
using System.Reflection;

namespace SiftQL.Schema;

public static class FilterCollectionFieldValues
{
    private const int MaxRuntimeArrayItems = 256;
    private const string TooManyRuntimeArrayItemsMessage =
        "Runtime array filters support at most 256 items.";

    public static object?[] Read(object? subject, string propertyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);

        string[] segments = propertyPath.Split('.');
        var values = new List<object?>();
        Collect(subject, segments, segmentIndex: 0, values);
        return values.ToArray();
    }

    private static void Collect(
        object? current,
        IReadOnlyList<string> segments,
        int segmentIndex,
        List<object?> values)
    {
        if (current is null)
        {
            if (segmentIndex == segments.Count)
                AddValue(values, null);
            return;
        }

        if (current is IEnumerable enumerable && current is not string)
        {
            foreach (object? item in enumerable)
                Collect(item, segments, segmentIndex, values);

            return;
        }

        if (segmentIndex == segments.Count)
        {
            AddValue(values, current);
            return;
        }

        MemberInfo? member = FindMember(current.GetType(), segments[segmentIndex]);
        if (member is null)
            return;

        Collect(ReadMember(current, member), segments, segmentIndex + 1, values);
    }

    private static void AddValue(List<object?> values, object? value)
    {
        if (values.Count >= MaxRuntimeArrayItems)
            throw TooManyRuntimeArrayItems();

        values.Add(value);
    }

    private static object? ReadMember(object instance, MemberInfo member) =>
        member switch
        {
            PropertyInfo property => property.GetValue(instance),
            FieldInfo field => field.GetValue(instance),
            _ => null,
        };

    private static MemberInfo? FindMember(Type type, string name)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            MemberInfo? member = FindDeclaredMember(current, name, ignoreCase: false) ??
                FindDeclaredMember(current, name, ignoreCase: true);
            if (member is not null)
                return member;
        }

        foreach (Type item in type.GetInterfaces())
        {
            MemberInfo? member = FindDeclaredMember(item, name, ignoreCase: false) ??
                FindDeclaredMember(item, name, ignoreCase: true);
            if (member is not null)
                return member;
        }

        return null;
    }

    private static MemberInfo? FindDeclaredMember(Type type, string name, bool ignoreCase)
    {
        StringComparison comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (PropertyInfo property in type.GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (string.Equals(property.Name, name, comparison) &&
                property.GetMethod is not null &&
                property.GetMethod.GetParameters().Length == 0)
            {
                return property;
            }
        }

        foreach (FieldInfo field in type.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (string.Equals(field.Name, name, comparison))
                return field;
        }

        return null;
    }

    private static InvalidOperationException TooManyRuntimeArrayItems() =>
        new(TooManyRuntimeArrayItemsMessage);
}

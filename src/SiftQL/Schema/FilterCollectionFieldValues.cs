using System.Collections;
using System.Reflection;

namespace SiftQL.Schema;

public static class FilterCollectionFieldValues
{
    private const int MaxRuntimeArrayItems = 256;
    private const string TooManyRuntimeArrayItemsMessage =
        "Runtime array filters support at most 256 items.";

    public static object?[]? Read(object? subject, string propertyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);

        string[] segments = propertyPath.Split('.');
        ValidateSegments(propertyPath, segments);
        var values = new List<object?>();
        int traversedItems = 0;
        if (Collect(subject, propertyPath, segments, segmentIndex: 0, values, ref traversedItems))
            return values.ToArray();

        return null;
    }

    private static void ValidateSegments(string propertyPath, IReadOnlyList<string> segments)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(segments[i]))
            {
                throw new ArgumentException(
                    $"Collection field property path '{propertyPath}' is malformed because it contains an empty segment.",
                    nameof(propertyPath));
            }
        }
    }

    private static bool Collect(
        object? current,
        string propertyPath,
        IReadOnlyList<string> segments,
        int segmentIndex,
        List<object?> values,
        ref int traversedItems)
    {
        if (current is null)
        {
            if (segmentIndex == segments.Count)
            {
                AddValue(values, null, propertyPath);
                return true;
            }
            return false;
        }

        if (current is IEnumerable enumerable && current is not string)
        {
            foreach (object? item in enumerable)
            {
                if (++traversedItems > MaxRuntimeArrayItems)
                    throw TooManyRuntimeArrayItems(propertyPath);

                Collect(item, propertyPath, segments, segmentIndex, values, ref traversedItems);
            }

            return true;
        }

        if (segmentIndex == segments.Count)
        {
            AddValue(values, current, propertyPath);
            return true;
        }

        MemberInfo? member = FindMember(current.GetType(), segments[segmentIndex]);
        if (member is null)
            return false;

        Type memberType = member switch
        {
            PropertyInfo prop => prop.PropertyType,
            FieldInfo field => field.FieldType,
            _ => typeof(object)
        };

        object? val = ReadMember(current, member);
        if (val is null)
        {
            if (memberType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(memberType))
                return false;

            if (segmentIndex + 1 == segments.Count)
            {
                AddValue(values, null, propertyPath);
                return true;
            }
            return false;
        }

        return Collect(val, propertyPath, segments, segmentIndex + 1, values, ref traversedItems);
    }

    private static void AddValue(List<object?> values, object? value, string propertyPath)
    {
        if (values.Count >= MaxRuntimeArrayItems)
            throw TooManyRuntimeArrayItems(propertyPath);

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

    private static InvalidOperationException TooManyRuntimeArrayItems(string propertyPath) =>
        new($"{TooManyRuntimeArrayItemsMessage} Property path: '{propertyPath}'.");
}

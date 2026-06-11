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
        return ReadCore(subject, propertyPath, members: null, declaringTypes: null);
    }

    internal static object?[]? Read(object? subject, string propertyPath, MemberInfo[] members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return ReadCore(subject, propertyPath, members, declaringTypes: null);
    }

    public static object?[]? Read(object? subject, string propertyPath, Type[] declaringTypes)
    {
        ArgumentNullException.ThrowIfNull(declaringTypes);
        return ReadCore(subject, propertyPath, members: null, declaringTypes);
    }

    private static object?[]? ReadCore(
        object? subject,
        string propertyPath,
        IReadOnlyList<MemberInfo>? members,
        IReadOnlyList<Type>? declaringTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);

        string[] segments = propertyPath.Split('.');
        ValidateSegments(propertyPath, segments);
        ValidateMemberPath(propertyPath, segments, members, declaringTypes);
        var values = new List<object?>();
        int traversedItems = 0;
        if (Collect(
                subject,
                propertyPath,
                segments,
                members,
                declaringTypes,
                segmentIndex: 0,
                values,
                ref traversedItems))
        {
            return values.ToArray();
        }

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

    private static void ValidateMemberPath(
        string propertyPath,
        IReadOnlyList<string> segments,
        IReadOnlyList<MemberInfo>? members,
        IReadOnlyList<Type>? declaringTypes)
    {
        if (members is not null && members.Count != segments.Count)
            throw MemberPathMismatch(propertyPath);
        if (declaringTypes is not null && declaringTypes.Count != segments.Count)
            throw MemberPathMismatch(propertyPath);
    }

    private static bool Collect(
        object? current,
        string propertyPath,
        IReadOnlyList<string> segments,
        IReadOnlyList<MemberInfo>? members,
        IReadOnlyList<Type>? declaringTypes,
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

                Collect(
                    item,
                    propertyPath,
                    segments,
                    members,
                    declaringTypes,
                    segmentIndex,
                    values,
                    ref traversedItems);
            }

            return true;
        }

        if (segmentIndex == segments.Count)
        {
            AddValue(values, current, propertyPath);
            return true;
        }

        MemberInfo? member = ResolveMember(
            current.GetType(),
            segments[segmentIndex],
            members,
            declaringTypes,
            segmentIndex);
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

        return Collect(
            val,
            propertyPath,
            segments,
            members,
            declaringTypes,
            segmentIndex + 1,
            values,
            ref traversedItems);
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

    private static MemberInfo? ResolveMember(
        Type runtimeType,
        string name,
        IReadOnlyList<MemberInfo>? members,
        IReadOnlyList<Type>? declaringTypes,
        int segmentIndex)
    {
        if (members is not null)
            return MemberCanRead(runtimeType, members[segmentIndex]) ? members[segmentIndex] : null;
        if (declaringTypes is not null)
            return FindMember(runtimeType, declaringTypes[segmentIndex], name);

        return FindMember(runtimeType, name);
    }

    private static bool MemberCanRead(Type runtimeType, MemberInfo member) =>
        member.DeclaringType is not null && member.DeclaringType.IsAssignableFrom(runtimeType);

    private static MemberInfo? FindMember(Type runtimeType, Type declaringType, string name)
    {
        if (!declaringType.IsAssignableFrom(runtimeType))
            return null;

        return FindDeclaredMember(declaringType, name, ignoreCase: false) ??
            FindDeclaredMember(declaringType, name, ignoreCase: true);
    }

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

    private static ArgumentException MemberPathMismatch(string propertyPath) =>
        new($"Collection field member path does not match property path '{propertyPath}'.");

    private static InvalidOperationException TooManyRuntimeArrayItems(string propertyPath) =>
        new($"{TooManyRuntimeArrayItemsMessage} Property path: '{propertyPath}'.");
}

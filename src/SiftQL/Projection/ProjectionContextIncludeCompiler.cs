using System.Globalization;
using System.Reflection;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Projection;

public static class ProjectionContextIncludeCompiler
{
    public static CompiledProjection<TContext>.IncludeProjector Compile<TContext>(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(include);
        if (EventProjectionConstantIntrinsics.IsConstant(include.Intrinsic))
            return CompileConstant<TContext>(include);

        if (!EventProjectionContextIntrinsics.TryParseMethod(
            include.Intrinsic,
            out string methodName,
            out string memberPath))
        {
            throw new FilterValidationException(
                $"Projection include '{include.Intrinsic}' is not a SiftQL context expression.");
        }

        MethodInfo method = ResolveMethod(typeof(TContext), methodName, include.Arguments.Length);
        ParameterInfo[] parameters = method.GetParameters();
        Func<object, object?>[] argumentGetters = CompileArgumentGetters(schema, include, parameters);
        MemberInfo[] members = ResolveMembers(method.ReturnType, memberPath);
        return new CompiledProjection<TContext>.IncludeProjector(
            include.ResultName,
            (subject, context, _) => new ValueTask<ProjectedEventValue>(
                Project(subject, context, method, argumentGetters, members)));
    }

    private static CompiledProjection<TContext>.IncludeProjector CompileConstant<TContext>(
        EventProjectionInclude include)
    {
        FilterValue value = ConstantArgument(include);
        ProjectedEventValue projected = ProjectedEventValue.FromScalar(FilterValueObject(value));
        return new CompiledProjection<TContext>.IncludeProjector(
            include.ResultName,
            (_, _, _) => new ValueTask<ProjectedEventValue>(projected));
    }

    private static FilterValue ConstantArgument(EventProjectionInclude include)
    {
        EventProjectionArgument? argument = include.Arguments
            .SingleOrDefault(static item =>
                string.Equals(
                    item.Name,
                    EventProjectionConstantIntrinsics.ArgumentName,
                    StringComparison.OrdinalIgnoreCase));
        if (argument?.Kind == EventProjectionArgumentKind.Value && argument.Value is not null)
            return argument.Value;

        throw new FilterValidationException(
            $"Projection include '{include.Intrinsic}' requires value argument '{EventProjectionConstantIntrinsics.ArgumentName}'.");
    }

    private static ProjectedEventValue Project<TContext>(
        object subject,
        TContext context,
        MethodInfo method,
        IReadOnlyList<Func<object, object?>> argumentGetters,
        IReadOnlyList<MemberInfo> members)
    {
        if (context is null)
            return ProjectedEventValue.Null;

        object?[] arguments = new object?[argumentGetters.Count];
        for (int i = 0; i < arguments.Length; i++)
        {
            object? argument = argumentGetters[i](subject);
            if (argument is MissingArgument)
                return ProjectedEventValue.Null;
            arguments[i] = argument;
        }

        object? current = method.Invoke(context, arguments);
        for (int i = 0; i < members.Count; i++)
        {
            if (current is null)
                return ProjectedEventValue.Null;

            current = ReadMember(current, members[i]);
        }

        return ProjectedEventValue.FromScalar(current);
    }

    private static Func<object, object?>[] CompileArgumentGetters(
        FilterSchema schema,
        EventProjectionInclude include,
        IReadOnlyList<ParameterInfo> parameters)
    {
        Dictionary<string, EventProjectionArgument> arguments = include.Arguments.ToDictionary(
            static argument => argument.Name,
            StringComparer.OrdinalIgnoreCase);
        var getters = new Func<object, object?>[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
        {
            ParameterInfo parameter = parameters[i];
            if (string.IsNullOrWhiteSpace(parameter.Name) ||
                !arguments.TryGetValue(parameter.Name, out EventProjectionArgument? argument))
            {
                throw new FilterValidationException(
                    $"Projection include '{include.Intrinsic}' is missing argument '{parameter.Name}'.");
            }

            Type targetType = parameter.ParameterType;
            getters[i] = argument.Kind == EventProjectionArgumentKind.SourceField
                ? CompileSourceGetter(schema, include, argument, targetType)
                : _ => ConvertValue(argument.Value, targetType);
        }

        return getters;
    }

    private static Func<object, object?> CompileSourceGetter(
        FilterSchema schema,
        EventProjectionInclude include,
        EventProjectionArgument argument,
        Type targetType)
    {
        if (!schema.TryGetField(argument.SourcePath, out FilterField field))
        {
            throw new FilterValidationException(
                $"Projection include '{include.Intrinsic}' source field '{argument.SourcePath}' is not supported by {schema.SubjectType.FullName}.");
        }

        return subject =>
        {
            object? value = field.Getter(subject);
            return value is null && IsRequiredValueType(targetType)
                ? MissingArgument.Instance
                : ConvertObject(value, targetType);
        };
    }

    private static MethodInfo ResolveMethod(Type contextType, string methodName, int argumentCount)
    {
        MethodInfo[] matches = contextType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == methodName && method.GetParameters().Length == argumentCount)
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FilterValidationException(
                $"Context type '{contextType.FullName}' does not define method '{methodName}' with {argumentCount} argument(s)."),
            _ => throw new FilterValidationException(
                $"Context type '{contextType.FullName}' has ambiguous method '{methodName}' with {argumentCount} argument(s)."),
        };
    }

    private static MemberInfo[] ResolveMembers(Type type, string memberPath)
    {
        if (string.IsNullOrWhiteSpace(memberPath))
            return [];

        string[] names = memberPath.Split('.');
        var members = new MemberInfo[names.Length];
        Type current = type;
        for (int i = 0; i < names.Length; i++)
        {
            MemberInfo member = ResolveMember(current, names[i]);
            members[i] = member;
            current = MemberType(member);
        }

        return members;
    }

    private static MemberInfo ResolveMember(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public) is { } property &&
        property.GetMethod?.GetParameters().Length == 0
            ? property
            : type.GetField(name, BindingFlags.Instance | BindingFlags.Public) is { } field
                ? field
                : throw new FilterValidationException(
                    $"Context expression member '{name}' is not supported by '{type.FullName}'.");

    private static Type MemberType(MemberInfo member) =>
        member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => typeof(object),
        };

    private static object? ReadMember(object instance, MemberInfo member) =>
        member switch
        {
            PropertyInfo property => property.GetValue(instance),
            FieldInfo field => field.GetValue(instance),
            _ => null,
        };

    private static object? ConvertValue(FilterValue value, Type targetType) =>
        value.Kind == FilterValueKind.Null && IsRequiredValueType(targetType)
            ? MissingArgument.Instance
            : ConvertObject(FilterValueObject(value), targetType);

    private static object? FilterValueObject(FilterValue value) =>
        value.Kind switch
        {
            FilterValueKind.Null => null,
            FilterValueKind.Boolean => value.Boolean,
            FilterValueKind.Integer => value.Integer,
            FilterValueKind.UnsignedInteger => value.UnsignedInteger,
            FilterValueKind.Number => value.Number,
            FilterValueKind.Decimal => value.Decimal,
            FilterValueKind.String => value.String,
            FilterValueKind.Guid => value.Guid,
            _ => null,
        };

    private static object? ConvertObject(object? value, Type targetType)
    {
        if (value is null)
            return null;

        Type nullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nullableTarget.IsInstanceOfType(value))
            return value;
        if (nullableTarget.IsEnum)
            return value is string text
                ? Enum.Parse(nullableTarget, text)
                : Enum.ToObject(nullableTarget, value);
        if (nullableTarget == typeof(Guid) && value is string guid)
            return Guid.Parse(guid);

        return Convert.ChangeType(value, nullableTarget, CultureInfo.InvariantCulture);
    }

    private static bool IsRequiredValueType(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null;

    private sealed class MissingArgument
    {
        public static MissingArgument Instance { get; } = new();
    }
}

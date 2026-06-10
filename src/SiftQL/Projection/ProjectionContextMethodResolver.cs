using System.Reflection;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;

namespace SiftQL.Projection;

internal static class ProjectionContextMethodResolver
{
    public static bool TryResolve<TContext>(
        FilterSchema schema,
        EventProjectionInclude include,
        out MethodInfo? method,
        out string memberPath)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (EventProjectionContextIntrinsics.TryParseMethod(
            include.Intrinsic,
            out string contextId,
            out string methodId,
            out memberPath))
        {
            method = ResolveQualifiedMethod(
                typeof(TContext),
                contextId,
                methodId,
                include.Arguments.Length);
            return true;
        }

        if (EventProjectionContextIntrinsics.TryParseLegacyMethod(
            include.Intrinsic,
            out string methodName,
            out memberPath))
        {
            method = ResolveMethod(typeof(TContext), methodName, include, schema);
            return true;
        }

        method = null;
        memberPath = string.Empty;
        return false;
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

    private static MethodInfo ResolveMethod(
        Type contextType,
        string methodName,
        EventProjectionInclude include,
        FilterSchema schema)
    {
        MethodInfo[] matches = contextType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == methodName && method.GetParameters().Length == include.Arguments.Length)
            .ToArray();
        if (matches.Length <= 1)
            return ResolveMethod(contextType, methodName, include.Arguments.Length);

        MethodInfo[] compatible = matches
            .Where(method => ArgumentsMatch(method.GetParameters(), include, schema))
            .ToArray();
        return compatible.Length switch
        {
            1 => compatible[0],
            0 => throw new FilterValidationException(
                $"Context type '{contextType.FullName}' does not define method '{methodName}' compatible with the include arguments."),
            _ => throw new FilterValidationException(
                $"Context type '{contextType.FullName}' has ambiguous method '{methodName}' with {include.Arguments.Length} argument(s)."),
        };
    }

    private static MethodInfo ResolveQualifiedMethod(
        Type contextType,
        string contextId,
        string methodId,
        int argumentCount)
    {
        if (!SiftQueryContextRegistry.TryGet(contextType, out SiftQueryContextDescriptor? descriptor) ||
            !string.Equals(descriptor.ContextId, contextId, StringComparison.Ordinal))
        {
            throw new FilterValidationException(
                $"Context type '{contextType.FullName}' does not have descriptor '{contextId}' registered.");
        }

        SiftQueryContextMethodDescriptor[] matches = descriptor.Methods
            .Where(method =>
                string.Equals(method.MethodId, methodId, StringComparison.Ordinal) &&
                method.Parameters.Count == argumentCount)
            .ToArray();

        return matches.Length switch
        {
            1 => ResolveMethod(contextType, matches[0]),
            0 => throw new FilterValidationException(
                $"Context descriptor '{contextId}' does not define method '{methodId}' with {argumentCount} argument(s)."),
            _ => throw new FilterValidationException(
                $"Context descriptor '{contextId}' has ambiguous method '{methodId}' with {argumentCount} argument(s)."),
        };
    }

    private static MethodInfo ResolveMethod(Type contextType, SiftQueryContextMethodDescriptor descriptor)
    {
        MethodInfo[] matches = contextType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method =>
                method.Name == descriptor.MethodName &&
                ParametersMatch(method.GetParameters(), descriptor.Parameters))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FilterValidationException(
                $"Context type '{contextType.FullName}' does not define descriptor method '{descriptor.MethodName}'."),
            _ => throw new FilterValidationException(
                $"Context type '{contextType.FullName}' has ambiguous descriptor method '{descriptor.MethodName}'."),
        };
    }

    private static bool ParametersMatch(
        IReadOnlyList<ParameterInfo> parameters,
        IReadOnlyList<SiftQueryContextParameterDescriptor> descriptors)
    {
        if (parameters.Count != descriptors.Count)
            return false;

        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].ParameterType != descriptors[i].Type)
                return false;
        }

        return true;
    }

    private static bool ArgumentsMatch(
        IReadOnlyList<ParameterInfo> parameters,
        EventProjectionInclude include,
        FilterSchema schema)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            ParameterInfo parameter = parameters[i];
            EventProjectionArgument? argument = include.Arguments.FirstOrDefault(item =>
                string.Equals(item.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (argument is null || !ArgumentMatches(parameter.ParameterType, argument, schema))
                return false;
        }

        return true;
    }

    private static bool ArgumentMatches(
        Type parameterType,
        EventProjectionArgument argument,
        FilterSchema schema)
    {
        if (argument.Kind == EventProjectionArgumentKind.SourceField)
        {
            return schema.TryGetField(argument.SourcePath, out FilterField field) &&
                TypesMatch(field.ValueType, parameterType);
        }

        return ValueMatches(argument.Value, parameterType);
    }

    private static bool TypesMatch(Type sourceType, Type parameterType)
    {
        Type source = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        Type target = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        return target.IsAssignableFrom(source);
    }

    private static bool ValueMatches(FilterValue value, Type parameterType)
    {
        Type target = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        return value.Kind switch
        {
            FilterValueKind.Null => !parameterType.IsValueType ||
                Nullable.GetUnderlyingType(parameterType) is not null,
            FilterValueKind.Boolean => target == typeof(bool),
            FilterValueKind.Integer => target.IsEnum || target == typeof(byte) ||
                target == typeof(sbyte) || target == typeof(short) ||
                target == typeof(ushort) || target == typeof(int) ||
                target == typeof(uint) || target == typeof(long) ||
                target == typeof(ulong),
            FilterValueKind.UnsignedInteger => target.IsEnum || target == typeof(byte) ||
                target == typeof(ushort) || target == typeof(uint) ||
                target == typeof(ulong),
            FilterValueKind.Number => target == typeof(float) || target == typeof(double),
            FilterValueKind.Decimal => target == typeof(decimal),
            FilterValueKind.String => target == typeof(string) ||
                target == typeof(Guid) ||
                target.IsEnum,
            FilterValueKind.Guid => target == typeof(Guid),
            FilterValueKind.Timestamp => target == typeof(DateTimeOffset) ||
                target == typeof(DateTime) ||
                target == typeof(DateOnly),
            _ => false,
        };
    }
}

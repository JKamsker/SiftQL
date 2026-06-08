using System.Reflection;
using SiftQL.Compiler;
using SiftQL.Expressions;

namespace SiftQL.Projection;

internal static class ProjectionContextMethodResolver
{
    public static bool TryResolve<TContext>(
        EventProjectionInclude include,
        out MethodInfo? method,
        out string memberPath)
    {
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
            method = ResolveMethod(typeof(TContext), methodName, include.Arguments.Length);
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
}

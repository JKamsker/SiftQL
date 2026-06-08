namespace SiftQL.Expressions;

public static class EventProjectionContextIntrinsics
{
    public const string MethodPrefix = "siftql.context.method:";
    public const string QualifiedMethodPrefix = "siftql.context:";
    private const string QualifiedMethodSeparator = ".method:";
    private const string QualifiedMemberSeparator = ".member:";

    public static string Method(string methodName, string memberPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        return string.IsNullOrWhiteSpace(memberPath)
            ? MethodPrefix + methodName
            : MethodPrefix + methodName + "." + memberPath;
    }

    public static string Method(string contextId, string methodId, string memberPath)
    {
        ValidateQualifiedPart(contextId, nameof(contextId));
        ValidateQualifiedPart(methodId, nameof(methodId));
        return string.IsNullOrWhiteSpace(memberPath)
            ? QualifiedMethodPrefix + contextId + QualifiedMethodSeparator + methodId
            : QualifiedMethodPrefix + contextId + QualifiedMethodSeparator + methodId +
                QualifiedMemberSeparator + memberPath;
    }

    public static bool TryParseMethod(
        string intrinsic,
        out string contextId,
        out string methodId,
        out string memberPath)
    {
        contextId = string.Empty;
        methodId = string.Empty;
        memberPath = string.Empty;
        if (!intrinsic.StartsWith(QualifiedMethodPrefix, StringComparison.Ordinal))
            return false;

        string value = intrinsic[QualifiedMethodPrefix.Length..];
        int methodSeparator = value.IndexOf(QualifiedMethodSeparator, StringComparison.Ordinal);
        if (methodSeparator <= 0)
            return false;

        contextId = value[..methodSeparator];
        string methodAndMember = value[(methodSeparator + QualifiedMethodSeparator.Length)..];
        int memberSeparator = methodAndMember.IndexOf(QualifiedMemberSeparator, StringComparison.Ordinal);
        if (memberSeparator < 0)
        {
            methodId = methodAndMember;
            return methodId.Length != 0;
        }

        methodId = methodAndMember[..memberSeparator];
        memberPath = methodAndMember[(memberSeparator + QualifiedMemberSeparator.Length)..];
        return methodId.Length != 0 && memberPath.Length != 0;
    }

    public static bool TryParseMethod(
        string intrinsic,
        out string methodName,
        out string memberPath) =>
        TryParseLegacyMethod(intrinsic, out methodName, out memberPath);

    public static bool TryParseLegacyMethod(
        string intrinsic,
        out string methodName,
        out string memberPath)
    {
        if (!intrinsic.StartsWith(MethodPrefix, StringComparison.Ordinal))
        {
            methodName = string.Empty;
            memberPath = string.Empty;
            return false;
        }

        string value = intrinsic[MethodPrefix.Length..];
        int separator = value.IndexOf('.');
        if (separator < 0)
        {
            methodName = value;
            memberPath = string.Empty;
            return methodName.Length != 0;
        }

        methodName = value[..separator];
        memberPath = value[(separator + 1)..];
        return methodName.Length != 0 && memberPath.Length != 0;
    }

    private static void ValidateQualifiedPart(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Contains(QualifiedMethodSeparator, StringComparison.Ordinal) ||
            value.Contains(QualifiedMemberSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Context intrinsic identifiers cannot contain reserved SiftQL separators.",
                parameterName);
        }
    }
}

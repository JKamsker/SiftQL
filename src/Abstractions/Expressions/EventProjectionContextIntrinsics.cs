namespace SiftQL.Expressions;

public static class EventProjectionContextIntrinsics
{
    public const string MethodPrefix = "siftql.context.method:";

    public static string Method(string methodName, string memberPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        return string.IsNullOrWhiteSpace(memberPath)
            ? MethodPrefix + methodName
            : MethodPrefix + methodName + "." + memberPath;
    }

    public static bool TryParseMethod(
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
}

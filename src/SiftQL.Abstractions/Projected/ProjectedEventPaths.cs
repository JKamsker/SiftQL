using SiftQL.Translation;

namespace SiftQL.Projected;

public static class ProjectedEventPaths
{
    public const string FieldPrefix = "field:";
    public const string ContextPrefix = "context:";

    public static string Field(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return FieldPrefix + name;
    }

    public static string Context(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ContextPrefix + name;
    }

    public static bool TrySplit(string path, out bool context, out string name)
    {
        if (path.StartsWith(FieldPrefix, StringComparison.Ordinal))
        {
            context = false;
            name = path[FieldPrefix.Length..];
            return name.Length != 0;
        }

        if (path.StartsWith(ContextPrefix, StringComparison.Ordinal))
        {
            context = true;
            name = path[ContextPrefix.Length..];
            return name.Length != 0;
        }

        context = false;
        name = string.Empty;
        return false;
    }
}

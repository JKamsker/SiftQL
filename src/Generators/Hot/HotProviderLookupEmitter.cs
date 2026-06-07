using System.Text;

namespace SiftQL.Generators.Hot;

internal static class HotProviderLookupEmitter
{
    public static void Emit(StringBuilder source, HotProviderSource provider)
    {
        EmitTryGet(
            source,
            provider,
            "TryGetFilter",
            "Func<object, bool>?",
            "predicate",
            "Filter_",
            static entry => entry.Kind == HotEntryKind.Filter && !IsParameterizedFilter(entry));
        EmitTryGet(
            source,
            provider,
            "TryGetParameterizedFilter",
            "ParameterizedHotFilterPredicate?",
            "predicate",
            "Filter_",
            IsParameterizedFilter);
        EmitTryGet(
            source,
            provider,
            "TryGetProjection",
            "Func<object, ProjectedEventField[]>?",
            "projectFields",
            "Projection_",
            static entry => entry.Kind == HotEntryKind.Projection && !IsParameterizedProjection(entry));
        EmitTryGet(
            source,
            provider,
            "TryGetParameterizedProjection",
            "ParameterizedHotProjectionFields?",
            "projectFields",
            "Projection_",
            IsParameterizedProjection);
    }

    private static void EmitTryGet(
        StringBuilder source,
        HotProviderSource provider,
        string method,
        string delegateType,
        string target,
        string prefix,
        Func<HotProviderEntry, bool> include)
    {
        source.Append("    public bool ").Append(method).Append("(Type subjectType, string fingerprint, out ");
        source.Append(delegateType).Append(' ').Append(target).AppendLine(")");
        source.AppendLine("    {");
        var entries = MatchingEntries(provider, prefix, include)
            .GroupBy(static entry => entry.Entry.Fingerprint, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal);

        source.AppendLine("        switch (fingerprint)");
        source.AppendLine("        {");
        foreach (var group in entries)
            EmitCase(source, group.Key, group, target);

        source.AppendLine("        }");
        source.Append("        ").Append(target).AppendLine(" = null;");
        source.AppendLine("        return false;");
        source.AppendLine("    }");
        source.AppendLine();
    }

    private static IEnumerable<LookupEntry> MatchingEntries(
        HotProviderSource provider,
        string prefix,
        Func<HotProviderEntry, bool> include)
    {
        for (int i = 0; i < provider.Entries.Count; i++)
        {
            HotProviderEntry entry = provider.Entries[i];
            if (include(entry))
                yield return new LookupEntry(entry, prefix + i);
        }
    }

    private static void EmitCase(
        StringBuilder source,
        string fingerprint,
        IEnumerable<LookupEntry> entries,
        string target)
    {
        source.Append("            case ");
        AppendLiteral(source, fingerprint);
        source.AppendLine(":");
        foreach (LookupEntry entry in entries.OrderBy(static item => item.Entry.SubjectTypeName, StringComparer.Ordinal))
            EmitSubjectMatch(source, entry, target);
        source.AppendLine("                break;");
    }

    private static void EmitSubjectMatch(StringBuilder source, LookupEntry lookup, string target)
    {
        source.Append("                if (subjectType == typeof(").Append(lookup.Entry.SubjectTypeName);
        source.AppendLine("))");
        source.AppendLine("                {");
        source.Append("                    ").Append(target).Append(" = ").Append(lookup.Method).AppendLine(";");
        source.AppendLine("                    return true;");
        source.AppendLine("                }");
    }

    private static bool IsParameterizedFilter(HotProviderEntry entry) =>
        entry.Kind == HotEntryKind.Filter &&
        entry.Filter is not null &&
        HotFilterParameterMap.Create(entry.Filter).HasParameters;

    private static bool IsParameterizedProjection(HotProviderEntry entry) =>
        entry.Kind == HotEntryKind.Projection && entry.Projection?.HasParameters == true;

    private static void AppendLiteral(StringBuilder source, string value)
    {
        CSharpStringLiteral.AppendTo(source, value);
    }

    private readonly record struct LookupEntry(HotProviderEntry Entry, string Method);
}

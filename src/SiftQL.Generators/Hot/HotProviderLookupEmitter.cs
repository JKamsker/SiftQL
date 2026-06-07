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
        for (int i = 0; i < provider.Entries.Count; i++)
        {
            HotProviderEntry entry = provider.Entries[i];
            if (include(entry))
                EmitMatch(source, entry, target, prefix + i);
        }

        source.Append("        ").Append(target).AppendLine(" = null;");
        source.AppendLine("        return false;");
        source.AppendLine("    }");
        source.AppendLine();
    }

    private static void EmitMatch(StringBuilder source, HotProviderEntry entry, string target, string method)
    {
        source.Append("        if (subjectType == typeof(").Append(entry.SubjectTypeName);
        source.Append(") && string.Equals(fingerprint, ");
        AppendLiteral(source, entry.Fingerprint);
        source.AppendLine(", StringComparison.Ordinal))");
        source.AppendLine("        {");
        source.Append("            ").Append(target).Append(" = ").Append(method).AppendLine(";");
        source.AppendLine("            return true;");
        source.AppendLine("        }");
        source.AppendLine();
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
}

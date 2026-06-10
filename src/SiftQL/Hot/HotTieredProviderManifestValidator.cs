using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Hot;

internal static class HotTieredProviderManifestValidator
{
    public static bool EntriesSatisfied(
        HotCompilationManifest manifest,
        AssemblyLoadContext loadContext,
        IReadOnlyList<IPrecompiledTieredProvider> providers)
    {
        if (manifest.Entries.Length == 0 || providers.Count == 0)
            return false;

        for (int i = 0; i < manifest.Entries.Length; i++)
        {
            HotCompilationManifestEntry entry = manifest.Entries[i];
            if (!TryResolveSubjectType(loadContext, entry.SubjectType, out Type? subjectType))
                return false;

            if (string.IsNullOrWhiteSpace(entry.Fingerprint) ||
                !EntrySatisfied(entry, subjectType, providers))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EntrySatisfied(
        HotCompilationManifestEntry entry,
        Type subjectType,
        IReadOnlyList<IPrecompiledTieredProvider> providers)
    {
        string[] fingerprints = CandidateFingerprints(entry, subjectType);
        for (int i = 0; i < providers.Count; i++)
        {
            for (int j = 0; j < fingerprints.Length; j++)
            {
                if (ProviderSatisfiesEntry(providers[i], entry, subjectType, fingerprints[j]))
                    return true;
            }
        }

        return false;
    }

    private static bool ProviderSatisfiesEntry(
        IPrecompiledTieredProvider provider,
        HotCompilationManifestEntry entry,
        Type subjectType,
        string fingerprint)
    {
        try
        {
            if (string.Equals(entry.Kind, "filter", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryEntryHasParameters(entry, out bool hasParameters))
                    return false;

                return hasParameters
                    ? provider.TryGetParameterizedFilter(subjectType, fingerprint, out var hot) &&
                        hot is not null
                    : provider.TryGetFilter(subjectType, fingerprint, out var predicate) &&
                        predicate is not null;
            }

            if (string.Equals(entry.Kind, "projection", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryEntryHasParameters(entry, out bool hasParameters))
                    return false;

                return hasParameters
                    ? provider.TryGetParameterizedProjection(subjectType, fingerprint, out var hot) &&
                        hot is not null
                    : provider.TryGetProjection(subjectType, fingerprint, out var projectFields) &&
                        projectFields is not null;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryEntryHasParameters(
        HotCompilationManifestEntry entry,
        out bool hasParameters)
    {
        try
        {
            if (string.Equals(entry.Kind, "filter", StringComparison.OrdinalIgnoreCase))
            {
                FilterExpression? filter = entry.Definition.Deserialize<FilterExpression>();
                hasParameters = filter is not null && HasParameters(filter);
                return filter is not null;
            }

            if (string.Equals(entry.Kind, "projection", StringComparison.OrdinalIgnoreCase))
            {
                EventProjectionExpression? projection =
                    entry.Definition.Deserialize<EventProjectionExpression>();
                hasParameters = projection is not null && HasParameters(projection);
                return projection is not null;
            }
        }
        catch
        {
        }

        hasParameters = false;
        return false;
    }

    private static bool HasParameters(FilterExpression expression)
    {
        if (HasParameter(expression.Value))
            return true;

        for (int i = 0; i < expression.Values.Length; i++)
        {
            if (HasParameter(expression.Values[i]))
                return true;
        }

        for (int i = 0; i < expression.Children.Length; i++)
        {
            if (HasParameters(expression.Children[i]))
                return true;
        }

        return false;
    }

    private static bool HasParameters(EventProjectionExpression projection)
    {
        for (int i = 0; i < projection.Includes.Length; i++)
        {
            EventProjectionArgument[] arguments = projection.Includes[i].Arguments;
            for (int j = 0; j < arguments.Length; j++)
            {
                if (arguments[j].Kind == EventProjectionArgumentKind.Value &&
                    HasParameter(arguments[j].Value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasParameter(FilterValue? value) =>
        !string.IsNullOrWhiteSpace(value?.ParameterKey);

    private static string[] CandidateFingerprints(
        HotCompilationManifestEntry entry,
        Type subjectType)
    {
        if (!string.Equals(entry.Kind, "projection", StringComparison.OrdinalIgnoreCase) ||
            !TryEffectiveProjectionFingerprint(entry, subjectType, out string? fingerprint) ||
            string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return [entry.Fingerprint];
        }

        return [entry.Fingerprint, fingerprint];
    }

    private static bool TryEffectiveProjectionFingerprint(
        HotCompilationManifestEntry entry,
        Type subjectType,
        [NotNullWhen(true)] out string? fingerprint)
    {
        try
        {
            EventProjectionExpression? projection =
                entry.Definition.Deserialize<EventProjectionExpression>();
            if (projection is null)
            {
                fingerprint = null;
                return false;
            }

            EventProjectionExpression effective = projection.Fields.Length == 0
                ? projection with { Fields = DefaultProjectionFields(subjectType) }
                : projection;
            fingerprint = ProjectionExpressionFingerprint.Create(effective);
            return true;
        }
        catch
        {
            fingerprint = null;
            return false;
        }
    }

    private static EventProjectionField[] DefaultProjectionFields(Type subjectType)
    {
        FilterSchema schema = subjectType == typeof(ProjectedEvent)
            ? ProjectedEventFilterSchema.ForProjection(EventProjectionExpression.Default)
            : FilterSchema.For(subjectType);
        return schema.FieldNames
            .Where(name => IsDefaultProjectionField(schema, name))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(static name => new EventProjectionField(name))
            .ToArray();
    }

    private static bool IsDefaultProjectionField(FilterSchema schema, string name) =>
        !IsVirtualMetadataField(schema.SubjectType, name) &&
        schema.TryGetField(name, out FilterField field) &&
        field.Kind != FilterFieldKind.Object;

    private static bool IsVirtualMetadataField(Type subjectType, string name) =>
        string.Equals(name, "subjectType", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "subjectName", StringComparison.OrdinalIgnoreCase) ||
        SubjectTypeMetadata.IsDiscriminatorPath(name) ||
        subjectType == typeof(ProjectedEvent) &&
        (string.Equals(name, "eventType", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "eventName", StringComparison.OrdinalIgnoreCase));

    private static bool TryResolveSubjectType(
        AssemblyLoadContext loadContext,
        string subjectType,
        [NotNullWhen(true)] out Type? type)
    {
        type = Type.GetType(
            subjectType,
            assemblyName =>
                ResolveLoadedAssembly(loadContext.Assemblies, assemblyName) ??
                ResolveLoadedAssembly(AppDomain.CurrentDomain.GetAssemblies(), assemblyName),
            (assembly, typeName, ignoreCase) =>
                assembly is null
                    ? FindLoadedType(loadContext.Assemblies, typeName, ignoreCase) ??
                        FindLoadedType(AppDomain.CurrentDomain.GetAssemblies(), typeName, ignoreCase)
                    : assembly.GetType(typeName, throwOnError: false, ignoreCase: ignoreCase),
            throwOnError: false);
        if (type is not null)
            return true;

        string fullName = TypeNameWithoutAssembly(subjectType);
        type = FindLoadedType(loadContext.Assemblies, fullName) ??
            FindLoadedType(AppDomain.CurrentDomain.GetAssemblies(), fullName);
        return type is not null;
    }

    private static Assembly? ResolveLoadedAssembly(
        IEnumerable<Assembly> assemblies,
        AssemblyName assemblyName)
    {
        foreach (Assembly assembly in assemblies)
        {
            AssemblyName current = assembly.GetName();
            if (string.Equals(current.Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                return assembly;
        }

        return null;
    }

    private static Type? FindLoadedType(
        IEnumerable<Assembly> assemblies,
        string fullName,
        bool ignoreCase = false)
    {
        foreach (Assembly assembly in assemblies)
        {
            Type? type = assembly.GetType(fullName, throwOnError: false, ignoreCase);
            if (type is not null)
                return type;
        }

        return null;
    }

    private static string TypeNameWithoutAssembly(string typeName)
    {
        int bracketDepth = 0;
        for (int i = 0; i < typeName.Length; i++)
        {
            char character = typeName[i];
            if (character == '[')
            {
                bracketDepth++;
            }
            else if (character == ']')
            {
                bracketDepth--;
            }
            else if (character == ',' && bracketDepth == 0)
            {
                return typeName[..i].Trim();
            }
        }

        return typeName.Trim();
    }
}

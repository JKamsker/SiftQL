using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using SiftQL.Schema;

namespace SiftQL.Hot;

public static class HotTieredProviderLoader
{
    private const string Schema = HotCompilationManifestCompatibility.Schema;
    private const string Engine = HotCompilationManifestCompatibility.Engine;
    private const string Generator = HotCompilationManifestCompatibility.Generator;
    private const string HashKey = "SiftQLHotManifestHash";
    private const string SchemaKey = "SiftQLHotManifestSchema";
    private const string EngineKey = "SiftQLHotFilterEngine";
    private const string GeneratorKey = "SiftQLHotGenerator";

    public static HotTieredProviderLoadResult TryLoad(HotTieredProviderLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            if (!File.Exists(options.ManifestPath) || !File.Exists(options.AssemblyPath))
            {
                return Result(
                    HotTieredProviderLoadStatus.MissingArtifact,
                    "Hot provider manifest or DLL does not exist.");
            }

            string manifestJson = File.ReadAllText(options.ManifestPath);
            HotCompilationManifest? manifest = DeserializeManifest(manifestJson);
            if (manifest is null)
                return Result(HotTieredProviderLoadStatus.InvalidManifest, "Hot provider manifest did not deserialize.");

            var version = ValidateManifest(manifest, options.RequireExactRuntimeVersion);
            if (version is not null)
                return version;

            string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
            IReadOnlyDictionary<string, string> metadata =
                HotTieredProviderAssemblyMetadata.Read(options.AssemblyPath);
            var metadataResult = ValidateAssemblyMetadata(metadata, manifestHash);
            if (metadataResult is not null)
                return metadataResult;

            string assemblyPath = Path.GetFullPath(options.AssemblyPath);
            var loadContext = new HotTieredProviderLoadContext(assemblyPath);
            Assembly? assembly = null;
            IDisposable registration;
            try
            {
                using var trackedRegistrations = PrecompiledTieredProviderRegistry.TrackRegistrations();
                assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                using var registrationScope = HotProviderRegistrationContext.AllowManifest(manifestHash);
                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
                trackedRegistrations.DisposeTrackedRegistrations();
                if (registrationScope.Commit() == 0)
                {
                    UnloadFailedAssembly(loadContext, assembly);
                    return Result(
                        HotTieredProviderLoadStatus.InvalidAssembly,
                        "Hot provider DLL did not register a provider for the manifest.");
                }

                trackedRegistrations.DisposeTrackedRegistrations();
                if (!ManifestEntriesSatisfied(
                    manifest,
                    loadContext,
                    registrationScope.CommittedProviders()))
                {
                    UnloadFailedAssembly(loadContext, assembly);
                    return Result(
                        HotTieredProviderLoadStatus.InvalidAssembly,
                        "Hot provider DLL did not satisfy the manifest entries.");
                }

                registration = registrationScope.ClaimCommittedRegistrations();
            }
            catch
            {
                UnloadFailedAssembly(loadContext, assembly);
                throw;
            }

            return new(
                HotTieredProviderLoadStatus.Loaded,
                $"Loaded hot provider DLL '{assemblyPath}'.",
                assembly,
                loadContext,
                registration);
        }
        catch (BadImageFormatException ex)
        {
            return Result(HotTieredProviderLoadStatus.InvalidAssembly, ex.Message);
        }
        catch (JsonException ex)
        {
            return Result(HotTieredProviderLoadStatus.InvalidManifest, ex.Message);
        }
        catch (Exception ex)
        {
            return Result(HotTieredProviderLoadStatus.LoadFailed, ex.Message);
        }
    }

    private static HotCompilationManifest? DeserializeManifest(string manifestJson) =>
        HotCompilationManifestCompatibility.HasRequiredFields(manifestJson)
            ? JsonSerializer.Deserialize<HotCompilationManifest>(manifestJson)
            : null;

    private static HotTieredProviderLoadResult? ValidateManifest(
        HotCompilationManifest manifest,
        bool requireExactRuntimeVersion)
    {
        if (manifest.Schema != Schema)
            return VersionMismatch("manifest schema", manifest.Schema, Schema);
        if (manifest.FilterEngineVersion != Engine)
            return VersionMismatch("filter engine", manifest.FilterEngineVersion, Engine);
        if (manifest.GeneratorVersion != Generator)
            return VersionMismatch("generator", manifest.GeneratorVersion, Generator);
        if (requireExactRuntimeVersion && manifest.RuntimeVersion != Environment.Version.ToString())
        {
            return VersionMismatch(
                "runtime",
                manifest.RuntimeVersion,
                Environment.Version.ToString());
        }

        return null;
    }

    private static HotTieredProviderLoadResult? ValidateAssemblyMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string manifestHash)
    {
        if (!metadata.TryGetValue(HashKey, out string? dllHash))
            return Result(HotTieredProviderLoadStatus.InvalidAssembly, "Hot provider DLL has no manifest hash.");
        if (!MetadataContains(dllHash, manifestHash, StringComparison.OrdinalIgnoreCase))
            return Result(HotTieredProviderLoadStatus.ManifestHashMismatch, "Hot provider manifest hash is stale.");
        if (!MetadataContains(metadata, SchemaKey, Schema, StringComparison.Ordinal) ||
            !MetadataContains(metadata, EngineKey, Engine, StringComparison.Ordinal) ||
            !MetadataContains(metadata, GeneratorKey, Generator, StringComparison.Ordinal))
        {
            return Result(HotTieredProviderLoadStatus.VersionMismatch, "Hot provider DLL version metadata is stale.");
        }

        return null;
    }

    private static bool ManifestEntriesSatisfied(
        HotCompilationManifest manifest,
        AssemblyLoadContext loadContext,
        IReadOnlyList<IPrecompiledTieredProvider> providers)
    {
        if (manifest.Entries.Length == 0 || providers.Count == 0)
            return false;

        for (int i = 0; i < manifest.Entries.Length; i++)
        {
            HotCompilationManifestEntry entry = manifest.Entries[i];
            if (!TryResolveSubjectType(loadContext, entry.SubjectType, out Type? subjectType) ||
                string.IsNullOrWhiteSpace(entry.Fingerprint) ||
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
        for (int i = 0; i < providers.Count; i++)
        {
            if (ProviderSatisfiesEntry(providers[i], entry, subjectType))
                return true;
        }

        return false;
    }

    private static bool ProviderSatisfiesEntry(
        IPrecompiledTieredProvider provider,
        HotCompilationManifestEntry entry,
        Type subjectType)
    {
        try
        {
            if (string.Equals(entry.Kind, "filter", StringComparison.Ordinal))
            {
                return provider.TryGetFilter(subjectType, entry.Fingerprint, out var predicate) &&
                    predicate is not null ||
                    provider.TryGetParameterizedFilter(subjectType, entry.Fingerprint, out var hot) &&
                    hot is not null;
            }

            if (string.Equals(entry.Kind, "projection", StringComparison.Ordinal))
            {
                return provider.TryGetProjection(subjectType, entry.Fingerprint, out var projectFields) &&
                    projectFields is not null ||
                    provider.TryGetParameterizedProjection(subjectType, entry.Fingerprint, out var hot) &&
                    hot is not null;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryResolveSubjectType(
        AssemblyLoadContext loadContext,
        string subjectType,
        out Type? type)
    {
        type = Type.GetType(subjectType, throwOnError: false);
        if (type is not null)
            return true;

        string fullName = subjectType.Split(',', 2)[0].Trim();
        type = FindLoadedType(AppDomain.CurrentDomain.GetAssemblies(), fullName) ??
            FindLoadedType(loadContext.Assemblies, fullName);
        return type is not null;
    }

    private static Type? FindLoadedType(IEnumerable<Assembly> assemblies, string fullName)
    {
        foreach (Assembly assembly in assemblies)
        {
            Type? type = assembly.GetType(fullName, throwOnError: false);
            if (type is not null)
                return type;
        }

        return null;
    }

    private static bool MetadataContains(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string expected,
        StringComparison comparison) =>
        metadata.TryGetValue(key, out string? value) &&
        MetadataContains(value, expected, comparison);

    private static bool MetadataContains(
        string values,
        string expected,
        StringComparison comparison)
    {
        foreach (string value in values.Split('\n'))
        {
            if (string.Equals(value, expected, comparison))
                return true;
        }

        return false;
    }

    private static HotTieredProviderLoadResult VersionMismatch(
        string name,
        string actual,
        string expected) =>
        Result(
            HotTieredProviderLoadStatus.VersionMismatch,
            $"Hot provider {name} '{actual}' does not match expected '{expected}'.");

    private static HotTieredProviderLoadResult Result(
        HotTieredProviderLoadStatus status,
        string message) =>
        new(status, message);

    private static void UnloadFailedAssembly(
        AssemblyLoadContext loadContext,
        Assembly? assembly)
    {
        if (assembly is not null)
        {
            FilterSchema.UnregisterGeneratedProvider(assembly);
            PrecompiledTieredProviderRegistry.RemoveAssembly(assembly);
        }

        loadContext.Unload();
    }

    private sealed class HotTieredProviderLoadContext(string assemblyPath)
        : AssemblyLoadContext(
            name: "SiftQLHotProvider:" + Path.GetFileNameWithoutExtension(assemblyPath),
            isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override System.Reflection.Assembly? Load(System.Reflection.AssemblyName assemblyName)
        {
            if (IsSharedRuntimeAssembly(assemblyName))
                return Assembly.Load(assemblyName);

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        private static bool IsSharedRuntimeAssembly(AssemblyName assemblyName) =>
            string.Equals(assemblyName.Name, "SiftQL", StringComparison.Ordinal) ||
            string.Equals(assemblyName.Name, "SiftQL.Abstractions", StringComparison.Ordinal);
    }
}

using System.Reflection;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Hot;

public static class PrecompiledTieredProviderRegistry
{
    private static readonly object s_gate = new();
    private static readonly AsyncLocal<ProviderScope?> s_scope = new();
    private static IPrecompiledTieredProvider[] s_providers = [];
    private static int s_globalVersion;

    internal static event Action? Changed;

    public static IDisposable CreateIsolatedScope()
    {
        var scope = new ProviderScope(s_scope.Value);
        s_scope.Value = scope;
        return scope;
    }

    public static IDisposable Register(IPrecompiledTieredProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ProviderScope? scope = s_scope.Value;
        if (scope is not null)
        {
            scope.Add(provider);
            IncrementGlobalVersion();
            return new ScopedRegistration(scope, provider);
        }

        lock (s_gate)
        {
            s_providers = [.. s_providers, provider];
            IncrementGlobalVersion();
        }

        return new Registration(provider);
    }

    internal static bool IsolatedScopeActive => s_scope.Value is not null;
    internal static int GlobalVersion => Volatile.Read(ref s_globalVersion);
    internal static bool HasProviders => Providers().Length != 0;

    internal static void RemoveAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        s_scope.Value?.RemoveAssembly(assembly);
        lock (s_gate)
        {
            IPrecompiledTieredProvider[] updated = s_providers
                .Where(item => !ReferenceEquals(item.GetType().Assembly, assembly))
                .ToArray();
            if (updated.Length == s_providers.Length)
                return;

            s_providers = updated;
            IncrementGlobalVersion();
        }
    }

    internal static bool TryGetFilter(
        Type subjectType,
        string fingerprint,
        out Func<object, bool>? predicate)
    {
        IPrecompiledTieredProvider[] providers = Providers();
        for (int i = providers.Length - 1; i >= 0; i--)
        {
            if (providers[i].TryGetFilter(subjectType, fingerprint, out predicate))
                return predicate is not null;
        }

        predicate = null;
        return false;
    }

    internal static bool TryGetParameterizedFilter(
        Type subjectType,
        string fingerprint,
        FilterValue[] parameters,
        out Func<object, bool>? predicate)
    {
        IPrecompiledTieredProvider[] providers = Providers();
        for (int i = providers.Length - 1; i >= 0; i--)
        {
            if (!providers[i].TryGetParameterizedFilter(subjectType, fingerprint, out var hot) ||
                hot is null)
            {
                continue;
            }

            predicate = subject => hot(subject, parameters);
            return true;
        }

        predicate = null;
        return false;
    }

    internal static bool TryGetProjection(
        Type subjectType,
        string fingerprint,
        out Func<object, ProjectedEventField[]>? projectFields)
    {
        IPrecompiledTieredProvider[] providers = Providers();
        for (int i = providers.Length - 1; i >= 0; i--)
        {
            if (providers[i].TryGetProjection(subjectType, fingerprint, out projectFields))
                return projectFields is not null;
        }

        projectFields = null;
        return false;
    }

    internal static bool TryGetParameterizedProjection(
        Type subjectType,
        string fingerprint,
        FilterValue[] parameters,
        out Func<object, ProjectedEventField[]>? projectFields)
    {
        IPrecompiledTieredProvider[] providers = Providers();
        for (int i = providers.Length - 1; i >= 0; i--)
        {
            if (!providers[i].TryGetParameterizedProjection(subjectType, fingerprint, out var hot) ||
                hot is null)
            {
                continue;
            }

            projectFields = subject => hot(subject, parameters);
            return true;
        }

        projectFields = null;
        return false;
    }

    private static IPrecompiledTieredProvider[] Providers() =>
        s_scope.Value?.Providers ?? Volatile.Read(ref s_providers);

    private static void IncrementGlobalVersion()
    {
        Interlocked.Increment(ref s_globalVersion);
        Changed?.Invoke();
    }

    private sealed class ProviderScope(ProviderScope? parent) : IDisposable
    {
        private readonly object _gate = new();
        private IPrecompiledTieredProvider[] _providers = [];

        public IPrecompiledTieredProvider[] Providers
        {
            get
            {
                lock (_gate)
                    return _providers;
            }
        }

        public void Add(IPrecompiledTieredProvider provider)
        {
            lock (_gate)
                _providers = [.. _providers, provider];
        }

        public void Remove(IPrecompiledTieredProvider provider)
        {
            lock (_gate)
                _providers = _providers.Where(item => !ReferenceEquals(item, provider)).ToArray();
            IncrementGlobalVersion();
        }

        public void RemoveAssembly(Assembly assembly)
        {
            bool changed;
            lock (_gate)
            {
                int before = _providers.Length;
                _providers = _providers
                    .Where(item => !ReferenceEquals(item.GetType().Assembly, assembly))
                    .ToArray();
                changed = _providers.Length != before;
            }

            if (changed)
                IncrementGlobalVersion();
        }

        public void Dispose()
        {
            if (!ReferenceEquals(s_scope.Value, this))
                return;

            bool hadProviders;
            lock (_gate)
                hadProviders = _providers.Length > 0;
            s_scope.Value = parent;
            if (hadProviders)
                IncrementGlobalVersion();
        }
    }

    private sealed class Registration(IPrecompiledTieredProvider provider) : IDisposable
    {
        public void Dispose()
        {
            lock (s_gate)
            {
                s_providers = s_providers.Where(item => !ReferenceEquals(item, provider)).ToArray();
                IncrementGlobalVersion();
            }
        }
    }

    private sealed class ScopedRegistration(ProviderScope scope, IPrecompiledTieredProvider provider) : IDisposable
    {
        public void Dispose() => scope.Remove(provider);
    }
}

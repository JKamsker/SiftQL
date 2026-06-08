using System.Reflection;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Hot;

public static class PrecompiledTieredProviderRegistry
{
    private static readonly object s_gate = new();
    private static readonly AsyncLocal<ProviderScope?> s_scope = new();
    private static readonly AsyncLocal<RegistrationTrackingScope?> s_tracker = new();
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
        return Register(provider, trackRegistration: true);
    }

    internal static IDisposable RegisterManifestProvider(IPrecompiledTieredProvider provider) =>
        Register(provider, trackRegistration: false);

    internal static RegistrationTrackingScope TrackRegistrations()
    {
        var scope = new RegistrationTrackingScope(s_tracker.Value);
        s_tracker.Value = scope;
        return scope;
    }

    private static IDisposable Register(
        IPrecompiledTieredProvider provider,
        bool trackRegistration)
    {
        ArgumentNullException.ThrowIfNull(provider);
        IDisposable registration;
        ProviderScope? scope = s_scope.Value;
        if (scope is { IsActive: true })
        {
            scope.Add(provider);
            IncrementGlobalVersion();
            registration = new ScopedRegistration(scope, provider);
            Track(registration, trackRegistration);
            return registration;
        }

        lock (s_gate)
        {
            s_providers = [.. s_providers, provider];
        }

        IncrementGlobalVersion();
        registration = new Registration(provider);
        Track(registration, trackRegistration);
        return registration;
    }

    internal static bool IsolatedScopeActive => s_scope.Value is { IsActive: true };
    internal static int GlobalVersion => Volatile.Read(ref s_globalVersion);
    internal static bool HasProviders => Providers().Length != 0;

    internal static void RemoveAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        s_scope.Value?.RemoveAssembly(assembly);
        bool changed;
        lock (s_gate)
        {
            IPrecompiledTieredProvider[] updated = s_providers
                .Where(item => !ReferenceEquals(item.GetType().Assembly, assembly))
                .ToArray();
            changed = updated.Length != s_providers.Length;
            if (changed)
                s_providers = updated;
        }

        if (changed)
            IncrementGlobalVersion();
    }

    internal static bool TryGetFilter(
        Type subjectType,
        string fingerprint,
        out Func<object, bool>? predicate)
    {
        IPrecompiledTieredProvider[] providers = Providers();
        for (int i = providers.Length - 1; i >= 0; i--)
        {
            if (!providers[i].TryGetFilter(subjectType, fingerprint, out predicate) ||
                predicate is null)
            {
                continue;
            }

            return true;
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
            if (!providers[i].TryGetProjection(subjectType, fingerprint, out projectFields) ||
                projectFields is null)
            {
                continue;
            }

            return true;
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
        s_scope.Value is { IsActive: true } scope
            ? scope.Providers
            : Volatile.Read(ref s_providers);

    private static void IncrementGlobalVersion()
    {
        Interlocked.Increment(ref s_globalVersion);
        Changed?.Invoke();
    }

    private static void Track(IDisposable registration, bool enabled)
    {
        if (enabled)
            s_tracker.Value?.Add(registration);
    }

    internal sealed class RegistrationTrackingScope(RegistrationTrackingScope? parent) : IDisposable
    {
        private readonly List<IDisposable> _registrations = [];
        private bool _disposed;

        public void Add(IDisposable registration)
        {
            if (!_disposed)
                _registrations.Add(registration);
        }

        public void DisposeTrackedRegistrations()
        {
            for (int i = _registrations.Count - 1; i >= 0; i--)
                _registrations[i].Dispose();
            _registrations.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            DisposeTrackedRegistrations();
            if (ReferenceEquals(s_tracker.Value, this))
                s_tracker.Value = parent;
        }
    }

    private sealed class ProviderScope(ProviderScope? parent) : IDisposable
    {
        private readonly object _gate = new();
        private IPrecompiledTieredProvider[] _providers = [];
        private int _disposed;

        public bool IsActive => Volatile.Read(ref _disposed) == 0;

        public IPrecompiledTieredProvider[] Providers
        {
            get
            {
                lock (_gate)
                    return _disposed == 0 ? _providers : [];
            }
        }

        public void Add(IPrecompiledTieredProvider provider)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                _providers = [.. _providers, provider];
            }
        }

        public void Remove(IPrecompiledTieredProvider provider)
        {
            bool changed;
            lock (_gate)
            {
                if (_disposed != 0)
                    return;

                changed = TryRemoveOne(ref _providers, provider);
            }

            if (changed)
                IncrementGlobalVersion();
        }

        public void RemoveAssembly(Assembly assembly)
        {
            bool changed;
            lock (_gate)
            {
                if (_disposed != 0)
                    return;

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
            bool hadProviders = false;
            lock (_gate)
            {
                if (_disposed == 0)
                {
                    _disposed = 1;
                    hadProviders = _providers.Length > 0;
                    _providers = [];
                }
            }

            if (ReferenceEquals(s_scope.Value, this))
                s_scope.Value = parent;
            if (hadProviders)
                IncrementGlobalVersion();
        }
    }

    private sealed class Registration(IPrecompiledTieredProvider provider) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            bool changed;
            lock (s_gate)
                changed = TryRemoveOne(ref s_providers, provider);

            if (changed)
                IncrementGlobalVersion();
        }
    }

    private sealed class ScopedRegistration(ProviderScope scope, IPrecompiledTieredProvider provider) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                scope.Remove(provider);
        }
    }

    private static bool TryRemoveOne(
        ref IPrecompiledTieredProvider[] providers,
        IPrecompiledTieredProvider provider)
    {
        int index = Array.FindLastIndex(providers, item => ReferenceEquals(item, provider));
        if (index < 0)
            return false;

        var updated = new IPrecompiledTieredProvider[providers.Length - 1];
        if (index > 0)
            Array.Copy(providers, 0, updated, 0, index);
        if (index < providers.Length - 1)
            Array.Copy(providers, index + 1, updated, index, providers.Length - index - 1);
        providers = updated;
        return true;
    }
}

namespace SiftQL.Hot;

public static class HotProviderRegistrationContext
{
    private static readonly AsyncLocal<string?> s_allowedManifestHash = new();
    private static readonly AsyncLocal<HotProviderRegistrationScope?> s_scope = new();

    public static IDisposable Register(IPrecompiledTieredProvider provider, string manifestHash)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestHash);
        if (!IsAllowed(manifestHash))
            return NullRegistration.Instance;

        HotProviderRegistrationScope? scope = s_scope.Value;
        return scope is null
            ? PrecompiledTieredProviderRegistry.Register(provider)
            : scope.Add(provider);
    }

    public static IDisposable RegisterFactory(
        Func<IPrecompiledTieredProvider> providerFactory,
        string manifestHash)
    {
        ArgumentNullException.ThrowIfNull(providerFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestHash);
        if (!IsAllowed(manifestHash))
            return NullRegistration.Instance;

        HotProviderRegistrationScope? scope = s_scope.Value;
        if (scope is not null)
            return scope.AddFactory(providerFactory);

        return Register(providerFactory(), manifestHash);
    }

    internal static HotProviderRegistrationScope AllowManifest(string manifestHash)
    {
        var scope = new HotProviderRegistrationScope(
            s_allowedManifestHash.Value,
            s_scope.Value);
        s_allowedManifestHash.Value = manifestHash;
        s_scope.Value = scope;
        return scope;
    }

    private static bool IsAllowed(string manifestHash)
    {
        string? allowed = s_allowedManifestHash.Value;
        return allowed is not null &&
            string.Equals(allowed, manifestHash, StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class HotProviderRegistrationScope(
        string? previousManifestHash,
        HotProviderRegistrationScope? previousScope) : IDisposable
    {
        private readonly List<PendingRegistration> _pending = [];
        private readonly List<IPrecompiledTieredProvider> _providers = [];
        private bool _committed;
        private bool _disposed;

        public IDisposable Add(IPrecompiledTieredProvider provider)
        {
            if (_disposed)
                return NullRegistration.Instance;

            var registration = new PendingRegistration(this, provider);
            _pending.Add(registration);
            return registration;
        }

        public IDisposable AddFactory(Func<IPrecompiledTieredProvider> providerFactory)
        {
            if (_disposed)
                return NullRegistration.Instance;

            var registration = new PendingRegistration(this, providerFactory);
            _pending.Add(registration);
            return registration;
        }

        public int Commit()
        {
            if (_disposed || _committed)
                return 0;

            int committed = 0;
            for (int i = 0; i < _pending.Count; i++)
                committed += _pending[i].Commit() ? 1 : 0;
            _pending.Clear();
            _committed = true;
            return committed;
        }

        internal IPrecompiledTieredProvider[] CommittedProviders() =>
            _providers.ToArray();

        internal IDisposable ClaimCommittedRegistrations(
            Func<IPrecompiledTieredProvider, IPrecompiledTieredProvider>? wrapProvider = null)
        {
            if (!_committed || _providers.Count == 0)
                return NullRegistration.Instance;

            var registrations = new IDisposable[_providers.Count];
            int registered = 0;
            try
            {
                for (; registered < _providers.Count; registered++)
                {
                    IPrecompiledTieredProvider sourceProvider = _providers[registered];
                    IPrecompiledTieredProvider provider = wrapProvider is null
                        ? sourceProvider
                        : wrapProvider(sourceProvider) ?? throw new ArgumentNullException(
                            nameof(wrapProvider),
                            "wrapProvider returned a null IPrecompiledTieredProvider.");
                    registrations[registered] =
                        PrecompiledTieredProviderRegistry.RegisterManifestProvider(provider);
                }
            }
            catch
            {
                for (int i = registered - 1; i >= 0; i--)
                    registrations[i].Dispose();
                throw;
            }

            _providers.Clear();
            return new CompositeRegistration(registrations);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            s_allowedManifestHash.Value = previousManifestHash;
            s_scope.Value = previousScope;
            for (int i = _pending.Count - 1; i >= 0; i--)
                _pending[i].Dispose();
            _pending.Clear();
            _providers.Clear();
        }

        private void RemovePending(PendingRegistration registration) =>
            _pending.Remove(registration);

        private void AddCommitted(IPrecompiledTieredProvider provider) =>
            _providers.Add(provider);

        private sealed class PendingRegistration(
            HotProviderRegistrationScope owner,
            IPrecompiledTieredProvider? provider,
            Func<IPrecompiledTieredProvider>? providerFactory) : IDisposable
        {
            private bool _committed;
            private bool _disposed;

            public PendingRegistration(
                HotProviderRegistrationScope owner,
                IPrecompiledTieredProvider provider)
                : this(owner, provider, providerFactory: null)
            {
            }

            public PendingRegistration(
                HotProviderRegistrationScope owner,
                Func<IPrecompiledTieredProvider> providerFactory)
                : this(owner, provider: null, providerFactory)
            {
            }

            public bool Commit()
            {
                if (_disposed || _committed)
                    return false;

                IPrecompiledTieredProvider item = provider ??
                    providerFactory!() ??
                    throw new ArgumentNullException(
                        nameof(providerFactory),
                        "providerFactory returned a null IPrecompiledTieredProvider.");
                _committed = true;
                owner.AddCommitted(item);
                return true;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                owner.RemovePending(this);
            }
        }
    }

    private sealed class NullRegistration : IDisposable
    {
        public static NullRegistration Instance { get; } = new();
        public void Dispose() { }
    }

    private sealed class CompositeRegistration(IDisposable[] registrations) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            for (int i = registrations.Length - 1; i >= 0; i--)
                registrations[i].Dispose();
        }
    }
}

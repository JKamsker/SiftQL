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
        private readonly List<IDisposable> _registrations = [];
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

        public void Commit()
        {
            if (_disposed || _committed)
                return;

            for (int i = 0; i < _pending.Count; i++)
                _pending[i].Commit();
            _pending.Clear();
            _committed = true;
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

            if (!_committed)
            {
                for (int i = _registrations.Count - 1; i >= 0; i--)
                    _registrations[i].Dispose();
            }
        }

        private void RemovePending(PendingRegistration registration) =>
            _pending.Remove(registration);

        private void AddCommitted(IDisposable registration) =>
            _registrations.Add(registration);

        private sealed class PendingRegistration(
            HotProviderRegistrationScope owner,
            IPrecompiledTieredProvider provider) : IDisposable
        {
            private IDisposable? _committed;
            private bool _disposed;

            public void Commit()
            {
                if (_disposed || _committed is not null)
                    return;

                IDisposable registration = PrecompiledTieredProviderRegistry.Register(provider);
                _committed = registration;
                owner.AddCommitted(registration);
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                owner.RemovePending(this);
                _committed?.Dispose();
            }
        }
    }

    private sealed class NullRegistration : IDisposable
    {
        public static NullRegistration Instance { get; } = new();
        public void Dispose() { }
    }
}

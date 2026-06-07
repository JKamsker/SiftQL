using SiftQL.Tiered;

namespace SiftQL.Kernel;

public sealed class CompiledKernelMatcher<TSubject>
{
    private readonly CompiledKernel _kernel;
    private Func<TSubject, bool> _matches;
    private bool _trackVersion;
    private int _version = -1;

    internal CompiledKernelMatcher(CompiledKernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _trackVersion = kernel.IsTiered;
        _matches = static _ => false;
        Refresh(kernel.Version);
    }

    public bool Matches(TSubject subject)
    {
        var matches = Volatile.Read(ref _matches);
        if (!Volatile.Read(ref _trackVersion))
            return matches(subject);

        int version = _kernel.Version;
        if (version != Volatile.Read(ref _version))
        {
            Refresh(version);
            matches = Volatile.Read(ref _matches);
        }

        return matches(subject);
    }

    private void Refresh(int version)
    {
        Func<TSubject, bool> matches;
        bool trackVersion = true;
        if (_kernel.IsAlwaysTrue)
        {
            matches = static _ => true;
            trackVersion = false;
        }
        else if (_kernel.TryGetTypedPredicate<TSubject>(out var typed))
        {
            matches = typed;
            if (_kernel.TieredSnapshot?.Tier == TieredKernelTier.Compiled)
                trackVersion = false;
        }
        else
        {
            matches = subject => _kernel.Matches(subject!);
        }

        Volatile.Write(ref _matches, matches);
        Volatile.Write(ref _trackVersion, trackVersion);
        Volatile.Write(ref _version, version);
    }
}

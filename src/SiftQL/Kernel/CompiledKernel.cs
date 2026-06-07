using SiftQL.Tiered;

namespace SiftQL.Kernel;

public sealed class CompiledKernel
{
    public static CompiledKernel Any { get; } = new(static _ => true, isBroad: true, isAlwaysTrue: true);

    private readonly TieredKernelState? _tieredState;
    private Func<object, bool> _predicate;
    private Delegate? _typedPredicate;
    private int _version;

    public CompiledKernel(Func<object, bool> predicate, bool isBroad)
        : this(KernelPredicate.FromObject(predicate), isBroad, tieredState: null)
    {
    }

    private CompiledKernel(Func<object, bool> predicate, bool isBroad, bool isAlwaysTrue)
        : this(KernelPredicate.FromObject(predicate), isBroad, tieredState: null)
    {
        IsAlwaysTrue = isAlwaysTrue;
    }

    internal CompiledKernel(
        Func<object, bool> predicate,
        bool isBroad,
        TieredKernelState? tieredState)
        : this(KernelPredicate.FromObject(predicate), isBroad, tieredState)
    {
    }

    internal CompiledKernel(
        KernelPredicate predicate,
        bool isBroad,
        TieredKernelState? tieredState = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicate = predicate.ObjectPredicate;
        _typedPredicate = predicate.TypedPredicate;
        _tieredState = tieredState;
        IsBroad = isBroad;
    }

    public bool IsBroad { get; }
    public bool IsTiered => _tieredState is not null;
    public TieredKernelSnapshot? TieredSnapshot => _tieredState?.Snapshot;
    internal bool IsAlwaysTrue { get; }
    internal int Version => Volatile.Read(ref _version);

    public bool Matches(object subject) =>
        Volatile.Read(ref _predicate)(subject);

    public bool Matches<TSubject>(TSubject subject)
    {
        if (Volatile.Read(ref _typedPredicate) is Func<TSubject, bool> typed)
            return typed(subject);

        return Volatile.Read(ref _predicate)(subject!);
    }

    public CompiledKernelMatcher<TSubject> CreateMatcher<TSubject>() =>
        new(this);

    internal bool TryGetTypedPredicate<TSubject>(out Func<TSubject, bool> predicate)
    {
        if (Volatile.Read(ref _typedPredicate) is Func<TSubject, bool> typed)
        {
            predicate = typed;
            return true;
        }

        predicate = null!;
        return false;
    }

    internal void Promote(KernelPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Volatile.Write(ref _typedPredicate, predicate.TypedPredicate);
        Volatile.Write(ref _predicate, predicate.ObjectPredicate);
        Interlocked.Increment(ref _version);
    }
}

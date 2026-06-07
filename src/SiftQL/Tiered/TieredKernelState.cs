using System.Diagnostics;
using SiftQL.Kernel;

namespace SiftQL.Tiered;

internal sealed class TieredKernelState
{
    private const int NotQueued = 0;
    private const int Queued = 1;
    private const int Compiled = 2;
    private const int Failed = 3;

    private readonly Func<KernelPredicate?> _compilePromoted;
    private readonly TieredFilterPromotionPolicy _promotionPolicy;
    private readonly Action<TieredKernelSnapshot>? _recordHot;
    private readonly Action<KernelPredicate> _publishPromoted;
    private readonly long _createdTimestamp;
    private readonly Func<object, bool> _interpreted;
    private long _evaluations;
    private long _matches;
    private int _compilationStatus;

    public TieredKernelState(
        Func<object, bool> interpreted,
        Func<KernelPredicate?> compilePromoted,
        TieredFilterPromotionPolicy promotionPolicy,
        Action<TieredKernelSnapshot>? recordHot,
        Action<KernelPredicate> publishPromoted)
    {
        ArgumentNullException.ThrowIfNull(interpreted);
        _compilePromoted = compilePromoted ?? throw new ArgumentNullException(nameof(compilePromoted));
        _promotionPolicy = promotionPolicy;
        _recordHot = recordHot;
        _publishPromoted = publishPromoted ?? throw new ArgumentNullException(nameof(publishPromoted));
        _createdTimestamp = Stopwatch.GetTimestamp();
        _interpreted = interpreted;
    }

    public bool Matches(object subject)
    {
        int status = Volatile.Read(ref _compilationStatus);
        if (status is Compiled or Failed)
            return _interpreted(subject);

        bool matches = _interpreted(subject);
        long evaluations = Interlocked.Increment(ref _evaluations);
        if (matches)
            Interlocked.Increment(ref _matches);
        TryQueuePromotion(evaluations);
        return matches;
    }

    private void TryQueuePromotion(long evaluations)
    {
        if (evaluations < _promotionPolicy.MinimumEvaluations)
            return;
        if (Volatile.Read(ref _compilationStatus) != NotQueued)
            return;
        if (Stopwatch.GetElapsedTime(_createdTimestamp) < _promotionPolicy.MinimumAge)
            return;
        if (Interlocked.CompareExchange(ref _compilationStatus, Queued, NotQueued) != NotQueued)
            return;
        try { _recordHot?.Invoke(Snapshot); }
        catch
        {
            // Hot-manifest recording is advisory; dispatch and promotion must continue.
        }
        if (!TieredPromotionQueue.TryQueue(CompileAndPromote, _promotionPolicy.QueueCapacity))
            Interlocked.CompareExchange(ref _compilationStatus, NotQueued, Queued);
    }

    private void CompileAndPromote()
    {
        try
        {
            KernelPredicate? compiled = _compilePromoted();
            if (compiled is null)
            {
                Volatile.Write(ref _compilationStatus, Failed);
                return;
            }

            _publishPromoted(compiled);
            Volatile.Write(ref _compilationStatus, Compiled);
        }
        catch
        {
            Volatile.Write(ref _compilationStatus, Failed);
        }
    }

    public TieredKernelSnapshot Snapshot =>
        CreateSnapshot();

    private TieredKernelSnapshot CreateSnapshot()
    {
        int status = Volatile.Read(ref _compilationStatus);
        return new TieredKernelSnapshot(
            status == Compiled ? TieredKernelTier.Compiled : TieredKernelTier.Interpreted,
            Interlocked.Read(ref _evaluations),
            Interlocked.Read(ref _matches),
            CompilationQueued: status == Queued,
            CompilationFailed: status == Failed);
    }
}

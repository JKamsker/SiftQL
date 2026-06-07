using System.Diagnostics;
using SiftQL;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Tiered;

namespace SiftQL.Projection;

internal sealed class TieredProjectionState<TContext>
{
    private const int NotQueued = 0;
    private const int Queued = 1;
    private const int Compiled = 2;
    private const int Failed = 3;
    private static readonly TimeSpan s_failedRetryDelay = TimeSpan.FromSeconds(30);

    private readonly Func<Func<object, ProjectedEventField[]>?> _compileProjectFields;
    private readonly TieredProjectionPromotionPolicy _promotionPolicy;
    private readonly Action<TieredProjectionSnapshot>? _recordHot;
    private readonly Action<Func<object, ProjectedEventField[]>> _publishProjectFields;
    private readonly long _createdTimestamp;
    private long _materializations;
    private long _payloadWrites;
    private int _compilationStatus;
    private int _failedProviderVersion;
    private long _failedTimestamp;

    public TieredProjectionState(
        Func<Func<object, ProjectedEventField[]>?> compileProjectFields,
        TieredProjectionPromotionPolicy promotionPolicy,
        Action<TieredProjectionSnapshot>? recordHot,
        Action<Func<object, ProjectedEventField[]>> publishProjectFields)
    {
        _compileProjectFields = compileProjectFields ??
            throw new ArgumentNullException(nameof(compileProjectFields));
        _promotionPolicy = promotionPolicy;
        _recordHot = recordHot;
        _publishProjectFields = publishProjectFields ??
            throw new ArgumentNullException(nameof(publishProjectFields));
        _createdTimestamp = Stopwatch.GetTimestamp();
    }

    public void RecordMaterialization()
    {
        if (!TryResetFailedPromotion())
            return;
        long operations = Interlocked.Increment(ref _materializations) +
            Interlocked.Read(ref _payloadWrites);
        TryQueuePromotion(operations);
    }

    public void RecordPayloadWrite()
    {
        if (!TryResetFailedPromotion())
            return;
        long operations = Interlocked.Increment(ref _payloadWrites) +
            Interlocked.Read(ref _materializations);
        TryQueuePromotion(operations);
    }

    private void TryQueuePromotion(long operations)
    {
        if (operations < _promotionPolicy.MinimumOperations)
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
            Func<object, ProjectedEventField[]>? compiled = _compileProjectFields();
            if (compiled is null)
            {
                MarkFailed();
                return;
            }

            _publishProjectFields(compiled);
            Volatile.Write(ref _compilationStatus, Compiled);
        }
        catch
        {
            MarkFailed();
        }
    }

    private void MarkFailed()
    {
        Volatile.Write(ref _failedProviderVersion, PrecompiledTieredProviderRegistry.GlobalVersion);
        Volatile.Write(ref _failedTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref _compilationStatus, Failed);
    }

    private bool TryResetFailedPromotion()
    {
        int status = Volatile.Read(ref _compilationStatus);
        if (status == Compiled)
            return false;
        if (status != Failed)
            return true;

        int failedProviderVersion = Volatile.Read(ref _failedProviderVersion);
        bool providerChanged = PrecompiledTieredProviderRegistry.GlobalVersion != failedProviderVersion;
        bool retryElapsed = Stopwatch.GetElapsedTime(Volatile.Read(ref _failedTimestamp)) >= s_failedRetryDelay;
        if (!providerChanged && !retryElapsed)
            return false;

        return Interlocked.CompareExchange(ref _compilationStatus, NotQueued, Failed) is Failed or NotQueued;
    }

    public TieredProjectionSnapshot Snapshot =>
        CreateSnapshot();

    private TieredProjectionSnapshot CreateSnapshot()
    {
        int status = Volatile.Read(ref _compilationStatus);
        return new TieredProjectionSnapshot(
            status == Compiled ? TieredProjectionTier.Compiled : TieredProjectionTier.Interpreted,
            Interlocked.Read(ref _materializations),
            Interlocked.Read(ref _payloadWrites),
            CompilationQueued: status == Queued,
            CompilationFailed: status == Failed);
    }
}

using System.Diagnostics;
using SiftQL;
using SiftQL.Projected;
using SiftQL.Tiered;

namespace SiftQL.Projection;

internal sealed class TieredProjectionState<TContext>
{
    private const int NotQueued = 0;
    private const int Queued = 1;
    private const int Compiled = 2;
    private const int Failed = 3;

    private readonly Func<Func<object, ProjectedEventField[]>?> _compileProjectFields;
    private readonly TieredProjectionPromotionPolicy _promotionPolicy;
    private readonly Action<TieredProjectionSnapshot>? _recordHot;
    private readonly Action<Func<object, ProjectedEventField[]>> _publishProjectFields;
    private readonly long _createdTimestamp;
    private long _materializations;
    private long _payloadWrites;
    private int _compilationStatus;

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
        int status = Volatile.Read(ref _compilationStatus);
        if (status is Compiled or Failed)
            return;
        long operations = Interlocked.Increment(ref _materializations) +
            Interlocked.Read(ref _payloadWrites);
        TryQueuePromotion(operations);
    }

    public void RecordPayloadWrite()
    {
        int status = Volatile.Read(ref _compilationStatus);
        if (status is Compiled or Failed)
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
                Volatile.Write(ref _compilationStatus, Failed);
                return;
            }

            _publishProjectFields(compiled);
            Volatile.Write(ref _compilationStatus, Compiled);
        }
        catch
        {
            Volatile.Write(ref _compilationStatus, Failed);
        }
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

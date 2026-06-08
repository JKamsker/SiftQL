using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projection;

namespace SiftQL.Hot;

public sealed class RuntimeHotProviderBatchSink : ITieredHotManifestSink, IDisposable
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly Dictionary<string, RuntimeHotProviderBatchEntry> _entries = new(StringComparer.Ordinal);
    private readonly ITieredHotManifestSink? _inner;
    private readonly IRuntimeHotProviderBatchQueue _queue;
    private readonly RuntimeHotProviderBatchOptions _options;
    private Timer? _delayedDrain;
    private DateTimeOffset _nextEligibleUtc;
    private bool _drainScheduled;
    private int _disposed;

    public RuntimeHotProviderBatchSink(
        IRuntimeHotProviderBatchQueue queue,
        RuntimeHotProviderBatchOptions? options = null,
        ITieredHotManifestSink? inner = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _options = options ?? new RuntimeHotProviderBatchOptions();
        _inner = inner;
        Validate(_options);
    }

    public void RecordHotFilter(
        Type subjectType,
        FilterExpression expression,
        long evaluations,
        long matches)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (HotManifestExpressionGuards.ContainsNonFiniteNumber(expression))
            return;

        string fingerprint = FilterExpressionFingerprint.CreateKey(expression).ToString();
        Record(
            "filter",
            subjectType,
            fingerprint,
            JsonSerializer.SerializeToElement(expression, s_json));
        try { _inner?.RecordHotFilter(subjectType, expression, evaluations, matches); }
        catch
        {
            // Mirroring to another sink is advisory; local hot-provider batching must continue.
        }
    }

    public void RecordHotProjection(
        Type subjectType,
        EventProjectionExpression projection,
        long materializations,
        long payloadWrites)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (HotManifestExpressionGuards.ContainsNonFiniteNumber(projection))
            return;

        string fingerprint = ProjectionExpressionFingerprint.CreateKey(projection).ToString();
        Record(
            "projection",
            subjectType,
            fingerprint,
            JsonSerializer.SerializeToElement(projection, s_json));
        try { _inner?.RecordHotProjection(subjectType, projection, materializations, payloadWrites); }
        catch
        {
            // Mirroring to another sink is advisory; local hot-provider batching must continue.
        }
    }

    private void Record(
        string kind,
        Type subjectType,
        string fingerprint,
        JsonElement definition)
    {
        RuntimeHotProviderBatch? batch = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string key = string.Concat(kind, "|", subjectType.AssemblyQualifiedName, "|", fingerprint);
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            _entries[key] = new RuntimeHotProviderBatchEntry
            {
                Key = key,
                Kind = kind,
                SubjectType = subjectType.AssemblyQualifiedName ?? subjectType.FullName ?? subjectType.Name,
                Fingerprint = fingerprint,
                Definition = definition,
            };
            if (_entries.Count >= _options.MinimumEntries && now >= _nextEligibleUtc)
                batch = DrainBatchLocked(now);
            else if (_entries.Count >= _options.MinimumEntries)
                ScheduleDelayedDrainLocked(now);
        }

        if (batch is not null)
            QueueOffThread(batch);
    }

    private RuntimeHotProviderBatch DrainBatchLocked(DateTimeOffset now)
    {
        RuntimeHotProviderBatchEntry[] entries = _entries.Values
            .OrderBy(static entry => entry.Kind, StringComparer.Ordinal)
            .ThenBy(static entry => entry.SubjectType, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Fingerprint, StringComparer.Ordinal)
            .Take(_options.MaxEntries)
            .ToArray();
        for (int i = 0; i < entries.Length; i++)
            _entries.Remove(entries[i].Key);
        _nextEligibleUtc = now + _options.MinimumInterval;
        return new RuntimeHotProviderBatch(Guid.NewGuid(), now, entries);
    }

    private void ScheduleDelayedDrainLocked(DateTimeOffset now)
    {
        if (_drainScheduled)
            return;

        TimeSpan delay = _nextEligibleUtc - now;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        _drainScheduled = true;
        _delayedDrain?.Dispose();
        _delayedDrain = new Timer(
            static state => ((RuntimeHotProviderBatchSink)state!).DrainDelayed(),
            this,
            delay,
            Timeout.InfiniteTimeSpan);
    }

    private void DrainDelayed()
    {
        RuntimeHotProviderBatch? batch = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            _drainScheduled = false;
            if (_entries.Count < _options.MinimumEntries)
                return;

            if (now >= _nextEligibleUtc)
                batch = DrainBatchLocked(now);
            else
                ScheduleDelayedDrainLocked(now);
        }

        if (batch is not null)
            QueueOffThread(batch);
    }

    private void QueueOffThread(RuntimeHotProviderBatch batch)
    {
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var work = (QueueWork)state!;
            try
            {
                work.Queue.Queue(work.Batch);
                work.Sink.DrainReadyBacklog();
            }
            catch
            {
                work.Sink.Requeue(work.Batch);
            }
        }, new QueueWork(this, _queue, batch));
    }

    private void DrainReadyBacklog()
    {
        RuntimeHotProviderBatch? batch = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            if (_entries.Count < _options.MinimumEntries)
                return;

            if (now >= _nextEligibleUtc)
                batch = DrainBatchLocked(now);
            else
                ScheduleDelayedDrainLocked(now);
        }

        if (batch is not null)
            QueueOffThread(batch);
    }

    private void Requeue(RuntimeHotProviderBatch batch)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            for (int i = 0; i < batch.Entries.Length; i++)
                _entries.TryAdd(batch.Entries[i].Key, batch.Entries[i]);
            _nextEligibleUtc = DateTimeOffset.MinValue;
            if (_entries.Count >= _options.MinimumEntries)
                ScheduleDelayedDrainLocked(DateTimeOffset.UtcNow);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        lock (_gate)
        {
            _delayedDrain?.Dispose();
            _delayedDrain = null;
            _drainScheduled = false;
            _entries.Clear();
        }
    }

    private static void Validate(RuntimeHotProviderBatchOptions options)
    {
        if (options.MinimumEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumEntries));
        if (options.MaxEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MaxEntries));
        if (options.MinimumInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumInterval));
    }

    private sealed record QueueWork(
        RuntimeHotProviderBatchSink Sink,
        IRuntimeHotProviderBatchQueue Queue,
        RuntimeHotProviderBatch Batch);
}

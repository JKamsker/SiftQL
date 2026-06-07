using System.Text.Json;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projection;

namespace SiftQL.Hot;

public sealed class RuntimeHotProviderBatchSink : ITieredHotManifestSink
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly Dictionary<string, RuntimeHotProviderBatchEntry> _entries = new(StringComparer.Ordinal);
    private readonly ITieredHotManifestSink? _inner;
    private readonly IRuntimeHotProviderBatchQueue _queue;
    private readonly RuntimeHotProviderBatchOptions _options;
    private DateTimeOffset _nextEligibleUtc;

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
        _inner?.RecordHotFilter(subjectType, expression, evaluations, matches);
        string fingerprint = FilterExpressionFingerprint.CreateKey(expression).ToString();
        Record(
            "filter",
            subjectType,
            fingerprint,
            JsonSerializer.SerializeToElement(expression, s_json));
    }

    public void RecordHotProjection(
        Type subjectType,
        EventProjectionExpression projection,
        long materializations,
        long payloadWrites)
    {
        _inner?.RecordHotProjection(subjectType, projection, materializations, payloadWrites);
        string fingerprint = ProjectionExpressionFingerprint.CreateKey(projection).ToString();
        Record(
            "projection",
            subjectType,
            fingerprint,
            JsonSerializer.SerializeToElement(projection, s_json));
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

    private void QueueOffThread(RuntimeHotProviderBatch batch)
    {
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var work = (QueueWork)state!;
            try { work.Queue.Queue(work.Batch); }
            catch { work.Sink.Requeue(work.Batch); }
        }, new QueueWork(this, _queue, batch));
    }

    private void Requeue(RuntimeHotProviderBatch batch)
    {
        lock (_gate)
        {
            for (int i = 0; i < batch.Entries.Length; i++)
                _entries.TryAdd(batch.Entries[i].Key, batch.Entries[i]);
            _nextEligibleUtc = DateTimeOffset.MinValue;
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

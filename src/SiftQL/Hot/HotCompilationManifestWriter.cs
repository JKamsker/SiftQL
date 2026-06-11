using System.Text.Json;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projection;

namespace SiftQL.Hot;

public sealed class HotCompilationManifestWriter : ITieredHotManifestSink, IDisposable
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly Dictionary<string, HotCompilationManifestEntry> _entries = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly HotCompilationManifestWriterOptions _options;
    private int _writeQueued;
    private int _disposed;
    private long _version;

    public HotCompilationManifestWriter(
        string path,
        HotCompilationManifestWriterOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _options = options ?? new HotCompilationManifestWriterOptions();
        HotCompilationManifestFileOps.ValidateOptions(_options);
        LoadExisting();
    }

    public void RecordHotFilter(
        Type subjectType,
        FilterExpression expression,
        long evaluations,
        long matches)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        ArgumentNullException.ThrowIfNull(expression);
        if (HotManifestExpressionGuards.ContainsUnsupportedFilterNode(expression) ||
            HotManifestExpressionGuards.ContainsNonFiniteNumber(expression))
        {
            return;
        }

        string fingerprint = FilterExpressionFingerprint.CreateKey(expression).ToString();
        var observed = new HotCompilationObserved { Evaluations = evaluations, Matches = matches };
        Record("filter", subjectType, fingerprint, JsonSerializer.SerializeToElement(expression, s_json), observed);
    }

    public void RecordHotProjection(
        Type subjectType,
        EventProjectionExpression projection,
        long materializations,
        long payloadWrites)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        ArgumentNullException.ThrowIfNull(projection);
        if (HotManifestExpressionGuards.ContainsNonFiniteNumber(projection))
            return;

        string fingerprint = ProjectionExpressionFingerprint.CreateKey(projection).ToString();
        var observed = new HotCompilationObserved
        {
            Materializations = materializations,
            PayloadWrites = payloadWrites,
        };
        Record("projection", subjectType, fingerprint, JsonSerializer.SerializeToElement(projection, s_json), observed);
    }

    public void Flush()
    {
        HotCompilationManifest manifest;
        lock (_gate)
        {
            manifest = CreateManifestLocked(DateTimeOffset.UtcNow);
        }

        WriteManifest(manifest, skipIfDisposed: false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Flush();
    }

    private void Record(
        string kind,
        Type subjectType,
        string fingerprint,
        JsonElement definition,
        HotCompilationObserved observed)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string key = string.Concat(kind, "|", subjectType.AssemblyQualifiedName, "|", fingerprint);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _entries.TryGetValue(key, out HotCompilationManifestEntry? existing);
            _entries[key] = new HotCompilationManifestEntry
            {
                Key = key,
                Kind = kind,
                SubjectType = subjectType.AssemblyQualifiedName ?? subjectType.FullName ?? subjectType.Name,
                Fingerprint = fingerprint,
                Definition = definition,
                Observed = MergeObserved(existing?.Observed, observed, now),
            };
            DecayLocked(now);
            TrimLocked();
            _version++;
        }

        QueueWrite();
    }

    private static HotCompilationObserved MergeObserved(
        HotCompilationObserved? existing,
        HotCompilationObserved observed,
        DateTimeOffset now) =>
        observed with
        {
            Evaluations = Math.Max(existing?.Evaluations ?? 0, observed.Evaluations),
            Matches = Math.Max(existing?.Matches ?? 0, observed.Matches),
            Materializations = Math.Max(existing?.Materializations ?? 0, observed.Materializations),
            PayloadWrites = Math.Max(existing?.PayloadWrites ?? 0, observed.PayloadWrites),
            FirstSeenUtc = existing is null || existing.FirstSeenUtc == default
                ? now
                : existing.FirstSeenUtc,
            LastSeenUtc = now,
        };

    private void QueueWrite()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        if (Interlocked.CompareExchange(ref _writeQueued, 1, 0) != 0)
            return;

        ThreadPool.UnsafeQueueUserWorkItem(static state => _ = ((HotCompilationManifestWriter)state!).FlushQueuedAsync(), this);
    }

    private async Task FlushQueuedAsync()
    {
        try
        {
            if (_options.CoalesceDelay > TimeSpan.Zero)
                await Task.Delay(_options.CoalesceDelay).ConfigureAwait(false);

            if (Volatile.Read(ref _disposed) != 0)
            {
                Volatile.Write(ref _writeQueued, 0);
                return;
            }

            long version;
            HotCompilationManifest manifest;
            lock (_gate)
            {
                version = _version;
                manifest = CreateManifestLocked(DateTimeOffset.UtcNow);
            }

            WriteManifest(manifest, skipIfDisposed: true);
            Volatile.Write(ref _writeQueued, 0);
            if (Volatile.Read(ref _disposed) != 0)
                return;

            lock (_gate)
            {
                if (_version == version)
                    return;
            }

            QueueWrite();
        }
        catch
        {
            Volatile.Write(ref _writeQueued, 0);
        }
    }

    private HotCompilationManifest CreateManifestLocked(DateTimeOffset now) =>
        new()
        {
            GeneratedAtUtc = now,
            Entries = _entries.Values
                .OrderBy(static entry => entry.Kind, StringComparer.Ordinal)
                .ThenBy(static entry => entry.SubjectType, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Fingerprint, StringComparer.Ordinal)
                .ToArray(),
        };

    private void LoadExisting()
    {
        if (!File.Exists(_path))
            return;

        HotCompilationManifest? manifest;
        try
        {
            string json = File.ReadAllText(_path);
            if (!HotCompilationManifestCompatibility.HasRequiredFields(json))
                return;

            manifest = JsonSerializer.Deserialize<HotCompilationManifest>(
                json,
                s_json);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return;
        }

        if (manifest?.Entries is null || !IsCompatibleExistingManifest(manifest))
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (HotCompilationManifestEntry entry in manifest.Entries)
        {
            if (!IsValidExistingEntry(entry))
                continue;
            _entries[entry.Key] = entry;
        }

        DecayLocked(now);
        TrimLocked();
    }

    private static bool IsCompatibleExistingManifest(HotCompilationManifest manifest)
    {
        var current = new HotCompilationManifest();
        return string.Equals(manifest.Schema, current.Schema, StringComparison.Ordinal) &&
            string.Equals(manifest.RuntimeVersion, current.RuntimeVersion, StringComparison.Ordinal) &&
            string.Equals(manifest.FilterEngineVersion, current.FilterEngineVersion, StringComparison.Ordinal) &&
            string.Equals(manifest.GeneratorVersion, current.GeneratorVersion, StringComparison.Ordinal);
    }

    private static bool IsValidExistingEntry(HotCompilationManifestEntry entry)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(entry.Key) ||
                !IsSupportedKind(entry.Kind) ||
                string.IsNullOrWhiteSpace(entry.SubjectType) ||
                string.IsNullOrWhiteSpace(entry.Fingerprint) ||
                entry.Definition.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (string.Equals(entry.Kind, "filter", StringComparison.OrdinalIgnoreCase))
                return IsValidExistingFilter(entry);
            if (string.Equals(entry.Kind, "projection", StringComparison.OrdinalIgnoreCase))
                return IsValidExistingProjection(entry);
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    private static bool IsValidExistingFilter(HotCompilationManifestEntry entry)
    {
        FilterExpression? expression = entry.Definition.Deserialize<FilterExpression>(s_json);
        return expression is not null &&
            !HotManifestExpressionGuards.ContainsUnsupportedFilterNode(expression) &&
            !HotManifestExpressionGuards.ContainsNonFiniteNumber(expression) &&
            string.Equals(
                FilterExpressionFingerprint.CreateKey(expression).ToString(),
                entry.Fingerprint,
                StringComparison.Ordinal);
    }

    private static bool IsValidExistingProjection(HotCompilationManifestEntry entry)
    {
        EventProjectionExpression? projection = entry.Definition.Deserialize<EventProjectionExpression>(s_json);
        return projection is not null &&
            !HotManifestExpressionGuards.ContainsNonFiniteNumber(projection) &&
            string.Equals(
                ProjectionExpressionFingerprint.CreateKey(projection).ToString(),
                entry.Fingerprint,
                StringComparison.Ordinal);
    }

    private static bool IsSupportedKind(string kind) =>
        string.Equals(kind, "filter", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, "projection", StringComparison.OrdinalIgnoreCase);

    private void DecayLocked(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - _options.Retention;
        foreach (string key in _entries
            .Where(pair => pair.Value.Observed.LastSeenUtc < cutoff)
            .Select(static pair => pair.Key)
            .ToArray())
        {
            _entries.Remove(key);
        }
    }

    private void TrimLocked()
    {
        if (_entries.Count <= _options.MaxEntries)
            return;

        foreach (string key in _entries.Values
            .OrderByDescending(static entry => entry.Observed.LastSeenUtc)
            .Skip(_options.MaxEntries)
            .Select(static entry => entry.Key)
            .ToArray())
        {
            _entries.Remove(key);
        }
    }

    private void WriteManifest(HotCompilationManifest manifest, bool skipIfDisposed)
    {
        lock (_writeGate)
        {
            if (skipIfDisposed && Volatile.Read(ref _disposed) != 0)
                return;

            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temp = string.Concat(_path, ".", Guid.NewGuid().ToString("N"), ".tmp");
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(manifest, s_json));
                HotCompilationManifestFileOps.MoveTempIntoPlace(temp, _path);
            }
            finally
            {
                HotCompilationManifestFileOps.TryDeleteTemp(temp);
            }
        }
    }
}

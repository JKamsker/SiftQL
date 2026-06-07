using System.Text.Json;

namespace SiftQL.Hot;

public sealed record HotCompilationManifest
{
    public string Schema { get; init; } = "siftql.hot.v1";
    public string RuntimeVersion { get; init; } = Environment.Version.ToString();
    public string FilterEngineVersion { get; init; } = "tiered-v1";
    public string GeneratorVersion { get; init; } = "hot-sourcegen-v1";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public HotCompilationManifestEntry[] Entries { get; init; } = [];
}

public sealed record HotCompilationManifestEntry
{
    public string Key { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string SubjectType { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
    public JsonElement Definition { get; init; }
    public HotCompilationObserved Observed { get; init; } = new();
}

public sealed record HotCompilationObserved
{
    public long Evaluations { get; init; }
    public long Matches { get; init; }
    public long Materializations { get; init; }
    public long PayloadWrites { get; init; }
    public DateTimeOffset FirstSeenUtc { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
}

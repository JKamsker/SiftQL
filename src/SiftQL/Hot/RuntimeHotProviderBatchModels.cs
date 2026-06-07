using System.Text.Json;

namespace SiftQL.Hot;

public sealed record RuntimeHotProviderBatch(
    Guid BatchId,
    DateTimeOffset CreatedAtUtc,
    RuntimeHotProviderBatchEntry[] Entries);

public sealed record RuntimeHotProviderBatchEntry
{
    public string Key { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string SubjectType { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
    public JsonElement Definition { get; init; }
}

public sealed record RuntimeHotProviderBatchCompileResult(
    bool Success,
    string Message,
    string? AssemblyPath = null);

public sealed record RuntimeHotProviderBatchOptions
{
    public int MinimumEntries { get; init; } = 8;
    public int MaxEntries { get; init; } = 256;
    public TimeSpan MinimumInterval { get; init; } = TimeSpan.FromMinutes(5);
}

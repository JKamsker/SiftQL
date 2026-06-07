namespace SiftQL.Hot;

public sealed record HotCompilationManifestWriterOptions
{
    public int MaxEntries { get; init; } = 4096;
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan CoalesceDelay { get; init; } = TimeSpan.FromMilliseconds(50);
}

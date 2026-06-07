namespace SiftQL.Tiered;

public enum TieredKernelTier
{
    Interpreted,
    Compiled,
}

public sealed record TieredKernelSnapshot(
    TieredKernelTier Tier,
    long Evaluations,
    long Matches,
    bool CompilationQueued,
    bool CompilationFailed);

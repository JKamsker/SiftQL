namespace SiftQL.Projection;

public enum TieredProjectionTier
{
    Interpreted,
    Compiled,
}

public sealed record TieredProjectionSnapshot(
    TieredProjectionTier Tier,
    long Materializations,
    long PayloadWrites,
    bool CompilationQueued,
    bool CompilationFailed);

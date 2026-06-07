using SiftQL.Compiler;

namespace SiftQL.Projection;

public sealed record EventPipelineCompilerOptions
{
    public static EventPipelineCompilerOptions Immediate { get; } = new();

    public static EventPipelineCompilerOptions Tiered { get; } = new()
    {
        FilterOptions = FilterCompilerOptions.Tiered,
        ProjectionOptions = ProjectionCompilerOptions.Tiered,
    };

    public FilterCompilerOptions FilterOptions { get; init; } = FilterCompilerOptions.Immediate;
    public ProjectionCompilerOptions ProjectionOptions { get; init; } = ProjectionCompilerOptions.Immediate;
}

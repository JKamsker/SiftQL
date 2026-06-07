using SiftQL.Translation;

namespace SiftQL.Expressions;

public sealed record EventProjectionInclude
{
    public EventProjectionInclude()
    {
    }

    public EventProjectionInclude(
        string intrinsic,
        string resultName,
        params EventProjectionArgument[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intrinsic);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultName);
        Intrinsic = intrinsic;
        ResultName = resultName;
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    }

    public string Intrinsic { get; init; } = string.Empty;
    public string ResultName { get; init; } = string.Empty;
    public EventProjectionArgument[] Arguments { get; init; } = [];
}

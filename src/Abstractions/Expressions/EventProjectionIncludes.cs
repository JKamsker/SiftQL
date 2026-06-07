using SiftQL.Translation;

namespace SiftQL.Expressions;

public static class EventProjectionIncludes
{
    public static EventProjectionInclude Custom(
        string intrinsicName,
        string resultName,
        params EventProjectionArgument[] arguments) =>
        new(intrinsicName, resultName, arguments);
}

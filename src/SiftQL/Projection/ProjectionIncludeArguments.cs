using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Values;

namespace SiftQL.Projection;

public static class ProjectionIncludeArguments
{
    public static double RequiredDouble(
        EventProjectionInclude include,
        string name,
        Func<string, Exception>? errorFactory = null)
    {
        var value = RequiredValue(include, name, errorFactory);
        return value.Kind switch
        {
            FilterValueKind.Integer => value.Integer,
            FilterValueKind.UnsignedInteger => value.UnsignedInteger,
            FilterValueKind.Number => value.Number,
            FilterValueKind.Decimal => (double)value.Decimal,
            _ => throw InvalidArgument(include, name, "number", errorFactory),
        };
    }

    public static int RequiredInt(
        EventProjectionInclude include,
        string name,
        Func<string, Exception>? errorFactory = null)
    {
        var value = RequiredValue(include, name, errorFactory);
        if (value.Kind != FilterValueKind.Integer ||
            value.Integer < int.MinValue ||
            value.Integer > int.MaxValue)
        {
            throw InvalidArgument(include, name, "integer", errorFactory);
        }

        return (int)value.Integer;
    }

    public static string RequiredString(
        EventProjectionInclude include,
        string name,
        Func<string, Exception>? errorFactory = null)
    {
        var value = RequiredValue(include, name, errorFactory);
        return value.Kind == FilterValueKind.String && !string.IsNullOrWhiteSpace(value.String)
            ? value.String
            : throw InvalidArgument(include, name, "string", errorFactory);
    }

    private static FilterValue RequiredValue(
        EventProjectionInclude include,
        string name,
        Func<string, Exception>? errorFactory) =>
        include.Arguments.FirstOrDefault(arg => string.Equals(arg.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Value ??
        throw Error(errorFactory, $"Projection include '{include.Intrinsic}' is missing argument '{name}'.");

    private static Exception InvalidArgument(
        EventProjectionInclude include,
        string name,
        string expected,
        Func<string, Exception>? errorFactory) =>
        Error(errorFactory, $"Projection include '{include.Intrinsic}' argument '{name}' must be a {expected}.");

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);
}

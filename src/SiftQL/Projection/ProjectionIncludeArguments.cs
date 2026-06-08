using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Schema;
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

    public static string RequiredSourcePath(
        EventProjectionInclude include,
        string name,
        Func<string, Exception>? errorFactory = null)
    {
        EventProjectionArgument argument = RequiredArgument(include, name, errorFactory);
        return argument.Kind == EventProjectionArgumentKind.SourceField &&
            !string.IsNullOrWhiteSpace(argument.SourcePath)
            ? argument.SourcePath
            : throw InvalidArgument(include, name, "source field", errorFactory);
    }

    public static Func<object, object?> RequiredSourceGetter(
        FilterSchema schema,
        EventProjectionInclude include,
        string name,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        string path = RequiredSourcePath(include, name, errorFactory);
        if (!schema.TryGetField(path, out FilterField field))
        {
            throw Error(
                errorFactory,
                $"Projection include '{include.Intrinsic}' source field '{path}' is not supported by {schema.SubjectType.FullName}.");
        }

        return field.Getter;
    }

    private static FilterValue RequiredValue(
        EventProjectionInclude include,
        string name,
        Func<string, Exception>? errorFactory)
    {
        EventProjectionArgument argument = RequiredArgument(include, name, errorFactory);
        return argument.Kind == EventProjectionArgumentKind.Value
            ? argument.Value
            : throw InvalidArgument(include, name, "literal value", errorFactory);
    }

    private static EventProjectionArgument RequiredArgument(
        EventProjectionInclude include,
        string name,
        Func<string, Exception>? errorFactory) =>
        include.Arguments.FirstOrDefault(arg => string.Equals(arg.Name, name, StringComparison.OrdinalIgnoreCase)) ??
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

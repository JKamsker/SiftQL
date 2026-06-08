using System.Globalization;
using System.Text;
using SiftQL.Expressions;
using SiftQL.Values;

namespace SiftQL.Projection;

internal static class ProjectionCompilerKeyBuilder
{
    public static string Build<TContext>(
        IReadOnlyList<CompiledProjection<TContext>.FieldProjector> fields,
        IReadOnlyList<EventProjectionInclude> includes,
        string? includeCompilerKey = null)
    {
        string fieldKey = string.Concat(fields.Select(FieldKey));
        string includeKey = string.Concat(includes.Select(IncludeKey));
        string compilerKey = includes.Count == 0 ? string.Empty : "C" + KeyPart(includeCompilerKey);
        return string.Concat(
            "F",
            CountPart(fields.Count),
            fieldKey,
            "I",
            CountPart(includes.Count),
            includeKey,
            compilerKey);
    }

    private static string FieldKey<TContext>(CompiledProjection<TContext>.FieldProjector field) =>
        string.Concat("f", KeyPart(field.Path), KeyPart(field.Name));

    private static string IncludeKey(EventProjectionInclude include)
    {
        string args = string.Concat(include.Arguments
            .OrderBy(static arg => arg.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ArgumentKey));
        return string.Concat(
            "i",
            KeyPart(include.Intrinsic),
            KeyPart(include.ResultName),
            CountPart(include.Arguments.Length),
            args);
    }

    private static string ArgumentKey(EventProjectionArgument argument) =>
        string.Concat("a", KeyPart(argument.Name), KeyPart(ArgumentValueKey(argument.Value)));

    private static string ArgumentValueKey(FilterValue value)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(value.ParameterKey))
        {
            builder.Append("param:");
            FilterKeyText.AppendText(builder, value.ParameterKey);
            builder.Append('|');
        }

        builder.Append('{').Append((int)value.Kind).Append(':');
        AppendLiteral(builder, value);
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendLiteral(StringBuilder builder, FilterValue value)
    {
        switch (value.Kind)
        {
            case FilterValueKind.Boolean:
                builder.Append(value.Boolean ? '1' : '0');
                break;
            case FilterValueKind.Integer:
                builder.Append(value.Integer.ToString(CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.UnsignedInteger:
                builder.Append(value.UnsignedInteger.ToString(CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.Number:
                builder.Append("bits:");
                builder.Append(BitConverter.DoubleToInt64Bits(value.Number)
                    .ToString("X16", CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.Decimal:
                builder.Append(value.Decimal.ToString("G29", CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.String:
                FilterKeyText.AppendText(builder, value.String);
                break;
            case FilterValueKind.Guid:
                builder.Append(value.Guid.ToString("D"));
                break;
        }
    }

    private static string CountPart(int count) =>
        count.ToString(CultureInfo.InvariantCulture) + ":";

    private static string KeyPart(string? value) =>
        value is null
            ? "-1:"
            : string.Concat(value.Length.ToString(CultureInfo.InvariantCulture), ":", value);
}

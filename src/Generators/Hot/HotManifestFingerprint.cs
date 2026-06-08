using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SiftQL.Generators.Hot;

internal static class HotManifestFingerprint
{
    public static string Filter(HotFilterNode expression)
    {
        var builder = new StringBuilder();
        AppendFilter(builder, expression);
        return builder.ToString();
    }

    public static string Projection(HotProjection projection)
    {
        var builder = new StringBuilder();
        builder.Append("fields[").Append(projection.Fields.Count).Append(']');
        for (int i = 0; i < projection.Fields.Count; i++)
        {
            AppendText(builder, projection.Fields[i].Path);
            builder.Append("=>");
            AppendText(builder, projection.Fields[i].Name);
        }

        builder.Append("includes[").Append(projection.Includes.Count).Append(']');
        for (int i = 0; i < projection.Includes.Count; i++)
            AppendInclude(builder, projection.Includes[i]);

        return Sha256(builder.ToString());
    }

    private static void AppendFilter(StringBuilder builder, HotFilterNode expression)
    {
        builder.Append('(').Append((int)expression.Kind);
        switch (expression.Kind)
        {
            case HotFilterNodeKind.Compare:
                AppendText(builder, expression.Field);
                builder.Append(':').Append(expression.Operator);
                AppendValue(builder, expression.Value);
                break;
            case HotFilterNodeKind.In:
                AppendText(builder, expression.Field);
                AppendValues(builder, expression.Values);
                break;
            case HotFilterNodeKind.Exists:
                AppendText(builder, expression.Field);
                break;
            case HotFilterNodeKind.Contains:
                AppendText(builder, expression.Field);
                AppendValue(builder, expression.Value);
                break;
            case HotFilterNodeKind.And:
            case HotFilterNodeKind.Or:
            case HotFilterNodeKind.Not:
                AppendChildren(builder, expression.Children);
                break;
        }

        builder.Append(')');
    }

    private static void AppendChildren(StringBuilder builder, EquatableArray<HotFilterNode> children)
    {
        builder.Append('[').Append(children.Count).Append(']');
        for (int i = 0; i < children.Count; i++)
            AppendFilter(builder, children[i]);
    }

    private static void AppendValues(StringBuilder builder, EquatableArray<HotFilterValue> values)
    {
        builder.Append('[').Append(values.Count).Append(']');
        for (int i = 0; i < values.Count; i++)
            AppendValue(builder, values[i]);
    }

    private static void AppendInclude(StringBuilder builder, HotProjectionInclude include)
    {
        AppendText(builder, include.Intrinsic);
        builder.Append(':');
        AppendText(builder, include.ResultName);
        HotProjectionArgument[] arguments = include.Arguments.Items
            .OrderBy(static item => item.Name, StringComparer.Ordinal)
            .ToArray();
        builder.Append("args[").Append(arguments.Length).Append(']');
        for (int i = 0; i < arguments.Length; i++)
        {
            AppendText(builder, arguments[i].Name);
            builder.Append('=');
            builder.Append(((int)arguments[i].Kind).ToString(CultureInfo.InvariantCulture)).Append(':');
            if (arguments[i].Kind == HotProjectionArgumentKind.SourceField)
                AppendText(builder, arguments[i].SourcePath);
            else
                AppendValue(builder, arguments[i].Value, ValueMode.ProjectionArgument);
        }
    }

    private static void AppendValue(
        StringBuilder builder,
        HotFilterValue? value,
        ValueMode mode = ValueMode.Filter)
    {
        if (value is null)
        {
            builder.Append("missing");
            return;
        }

        builder.Append('{').Append((int)value.Kind).Append(':');
        if (!string.IsNullOrWhiteSpace(value.ParameterKey))
        {
            builder.Append("param:");
            AppendText(builder, value.ParameterKey);
            builder.Append('}');
            return;
        }

        AppendLiteral(builder, value, mode);
        builder.Append('}');
    }

    private static void AppendLiteral(
        StringBuilder builder,
        HotFilterValue value,
        ValueMode mode)
    {
        switch (value.Kind)
        {
            case HotFilterValueKind.Boolean:
                builder.Append(value.Boolean ? '1' : '0');
                break;
            case HotFilterValueKind.Integer:
                builder.Append(value.Integer.ToString(CultureInfo.InvariantCulture));
                break;
            case HotFilterValueKind.UnsignedInteger:
                builder.Append(value.UnsignedInteger.ToString(CultureInfo.InvariantCulture));
                break;
            case HotFilterValueKind.Number:
                AppendNumber(builder, value.Number, mode);
                break;
            case HotFilterValueKind.Decimal:
                builder.Append(value.Decimal.ToString("G29", CultureInfo.InvariantCulture));
                break;
            case HotFilterValueKind.String:
                AppendText(builder, value.String);
                break;
            case HotFilterValueKind.Guid:
                builder.Append(value.Guid);
                break;
        }
    }

    private static void AppendNumber(
        StringBuilder builder,
        double value,
        ValueMode mode)
    {
        if (mode == ValueMode.ProjectionArgument)
        {
            builder.Append("bits:");
            builder.Append(BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture));
            return;
        }

        builder.Append(value == 0D
            ? "0"
            : value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void AppendText(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-1:");
            return;
        }

        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
    }

    private static string Sha256(string text)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        var builder = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
            builder.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private enum ValueMode
    {
        Filter,
        ProjectionArgument,
    }
}

using System.Security.Cryptography;
using System.Text;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Values;

namespace SiftQL.Projection;

internal static class ProjectionExpressionFingerprint
{
    public static ProjectionExpressionKey CreateKey(EventProjectionExpression projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return ProjectionExpressionKey.From(projection);
    }

    public static string Create(EventProjectionExpression projection) =>
        CreateKey(projection).ToString();
}

internal sealed class ProjectionExpressionKey : IEquatable<ProjectionExpressionKey>
{
    private readonly int _hashCode;

    private ProjectionExpressionKey(
        StructuralKeyArray<ProjectionFieldKey> fields,
        StructuralKeyArray<ProjectionIncludeKey> includes)
    {
        Fields = fields;
        Includes = includes;
        _hashCode = HashCode.Combine(Fields, Includes);
    }

    public StructuralKeyArray<ProjectionFieldKey> Fields { get; }
    public StructuralKeyArray<ProjectionIncludeKey> Includes { get; }

    public static ProjectionExpressionKey From(EventProjectionExpression projection) =>
        new(
            projection.Fields.Length == 0
                ? StructuralKeyArray<ProjectionFieldKey>.Empty
                : StructuralKeyArray<ProjectionFieldKey>.From(projection.Fields, ProjectionFieldKey.From),
            projection.Includes.Length == 0
                ? StructuralKeyArray<ProjectionIncludeKey>.Empty
                : StructuralKeyArray<ProjectionIncludeKey>.From(projection.Includes, ProjectionIncludeKey.From));

    public bool Equals(ProjectionExpressionKey? other) =>
        ReferenceEquals(this, other) ||
        (other is not null &&
            Fields.Equals(other.Fields) &&
            Includes.Equals(other.Includes));

    public override bool Equals(object? obj) =>
        obj is ProjectionExpressionKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public override string ToString()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(ToDebugString()));
        return Convert.ToHexString(hash);
    }

    public string ToDebugString()
    {
        var builder = new StringBuilder();
        AppendTo(builder);
        return builder.ToString();
    }

    private void AppendTo(StringBuilder builder)
    {
        builder.Append("fields[").Append(Fields.Count).Append(']');
        for (int i = 0; i < Fields.Count; i++)
        {
            FilterKeyText.AppendText(builder, Fields[i].Path);
            builder.Append("=>");
            FilterKeyText.AppendText(builder, Fields[i].Name);
        }

        builder.Append("includes[").Append(Includes.Count).Append(']');
        for (int i = 0; i < Includes.Count; i++)
            Includes[i].AppendTo(builder);
    }
}

internal readonly record struct ProjectionFieldKey(string Path, string Name)
{
    public static ProjectionFieldKey From(EventProjectionField field) =>
        new(field.Path, field.Name);
}

internal readonly record struct ProjectionArgumentKey(
    string Name,
    EventProjectionArgumentKind Kind,
    ProjectionArgumentValueKey Value,
    string SourcePath)
{
    public static ProjectionArgumentKey From(EventProjectionArgument argument) =>
        new(
            argument.Name,
            argument.Kind,
            ProjectionArgumentValueKey.From(argument.Value),
            argument.SourcePath);
}

internal readonly struct ProjectionArgumentValueKey : IEquatable<ProjectionArgumentValueKey>
{
    private readonly bool _hasValue;

    private ProjectionArgumentValueKey(FilterValue value)
    {
        _hasValue = true;
        Kind = value.Kind;
        ParameterKey = string.IsNullOrWhiteSpace(value.ParameterKey) ? null : value.ParameterKey;
        Boolean = value.Boolean;
        Integer = value.Integer;
        UnsignedInteger = value.UnsignedInteger;
        NumberBits = BitConverter.DoubleToInt64Bits(value.Number);
        Decimal = value.Decimal;
        Text = value.String;
        Guid = value.Guid;
    }

    public FilterValueKind Kind { get; }
    public string? ParameterKey { get; }
    public bool Boolean { get; }
    public long Integer { get; }
    public ulong UnsignedInteger { get; }
    public long NumberBits { get; }
    public decimal Decimal { get; }
    public string? Text { get; }
    public Guid Guid { get; }

    public static ProjectionArgumentValueKey From(FilterValue? value) =>
        value is null ? default : new ProjectionArgumentValueKey(value);

    public bool Equals(ProjectionArgumentValueKey other) =>
        _hasValue == other._hasValue &&
        (!_hasValue ||
            Kind == other.Kind &&
            (HasParameter || other.HasParameter
                ? string.Equals(ParameterKey, other.ParameterKey, StringComparison.Ordinal)
                : EqualsLiteral(other)));

    public override bool Equals(object? obj) =>
        obj is ProjectionArgumentValueKey other && Equals(other);

    public override int GetHashCode()
    {
        if (!_hasValue)
            return 0;
        if (HasParameter)
            return HashCode.Combine(Kind, ParameterKey);

        return HashCode.Combine(
            Kind,
            Boolean,
            Integer,
            UnsignedInteger,
            NumberBits,
            Decimal,
            Text,
            Guid);
    }

    public void AppendTo(StringBuilder builder)
    {
        if (!_hasValue)
        {
            builder.Append("missing");
            return;
        }

        builder.Append('{').Append((int)Kind).Append(':');
        if (HasParameter)
        {
            builder.Append("param:");
            FilterKeyText.AppendText(builder, ParameterKey);
            builder.Append('}');
            return;
        }

        AppendLiteral(builder);
        builder.Append('}');
    }

    private bool HasParameter => !string.IsNullOrWhiteSpace(ParameterKey);

    private bool EqualsLiteral(ProjectionArgumentValueKey other) =>
        Boolean == other.Boolean &&
        Integer == other.Integer &&
        UnsignedInteger == other.UnsignedInteger &&
        NumberBits == other.NumberBits &&
        Decimal == other.Decimal &&
        string.Equals(Text, other.Text, StringComparison.Ordinal) &&
        Guid == other.Guid;

    private void AppendLiteral(StringBuilder builder)
    {
        switch (Kind)
        {
            case FilterValueKind.Boolean:
                builder.Append(Boolean ? '1' : '0');
                break;
            case FilterValueKind.Integer:
                builder.Append(Integer.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.UnsignedInteger:
                builder.Append(UnsignedInteger.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.Number:
                builder.Append("bits:");
                builder.Append(NumberBits.ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.Decimal:
                builder.Append(Decimal.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.String:
                FilterKeyText.AppendText(builder, Text);
                break;
            case FilterValueKind.Guid:
                builder.Append(Guid.ToString("D"));
                break;
        }
    }
}

internal readonly record struct ProjectionIncludeKey(
    string Intrinsic,
    string ResultName,
    StructuralKeyArray<ProjectionArgumentKey> Arguments)
{
    public static ProjectionIncludeKey From(EventProjectionInclude include) =>
        new(
            include.Intrinsic,
            include.ResultName,
            new StructuralKeyArray<ProjectionArgumentKey>(
                include.Arguments
                    .OrderBy(static item => item.Name, StringComparer.Ordinal)
                    .Select(ProjectionArgumentKey.From)));

    public void AppendTo(StringBuilder builder)
    {
        FilterKeyText.AppendText(builder, Intrinsic);
        builder.Append(':');
        FilterKeyText.AppendText(builder, ResultName);
        builder.Append("args[").Append(Arguments.Count).Append(']');
        for (int i = 0; i < Arguments.Count; i++)
        {
            FilterKeyText.AppendText(builder, Arguments[i].Name);
            builder.Append('=');
            builder.Append((int)Arguments[i].Kind).Append(':');
            if (Arguments[i].Kind == EventProjectionArgumentKind.SourceField)
                FilterKeyText.AppendText(builder, Arguments[i].SourcePath);
            else
                Arguments[i].Value.AppendTo(builder);
        }
    }
}

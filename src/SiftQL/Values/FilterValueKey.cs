using System.Globalization;
using System.Text;
using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Values;

internal readonly struct FilterValueKey : IEquatable<FilterValueKey>
{
    private readonly bool _hasValue;

    private FilterValueKey(FilterValue value)
    {
        _hasValue = true;
        Kind = value.Kind;
        ParameterKey = string.IsNullOrWhiteSpace(value.ParameterKey) ? null : value.ParameterKey;
        Boolean = value.Boolean;
        Integer = value.Integer;
        UnsignedInteger = value.UnsignedInteger;
        Number = value.Number;
        Decimal = value.Decimal;
        Text = value.String;
        Guid = value.Guid;
    }

    public FilterValueKind Kind { get; }
    public string? ParameterKey { get; }
    public bool Boolean { get; }
    public long Integer { get; }
    public ulong UnsignedInteger { get; }
    public double Number { get; }
    public decimal Decimal { get; }
    public string? Text { get; }
    public Guid Guid { get; }

    public static FilterValueKey From(FilterValue? value) =>
        value is null ? default : new FilterValueKey(value);

    public bool Equals(FilterValueKey other) =>
        _hasValue == other._hasValue &&
        (!_hasValue ||
            (Kind == other.Kind && EqualsValue(other)));

    public override bool Equals(object? obj) =>
        obj is FilterValueKey other && Equals(other);

    public override int GetHashCode()
    {
        if (!_hasValue)
            return 0;
        if (!string.IsNullOrWhiteSpace(ParameterKey))
            return HashCode.Combine(Kind, ParameterKey);

        return HashCode.Combine(
            Kind,
            Boolean,
            Integer,
            UnsignedInteger,
            Number,
            Decimal,
            Text,
            Guid);
    }

    private bool EqualsValue(FilterValueKey other)
    {
        if (!string.IsNullOrWhiteSpace(ParameterKey) ||
            !string.IsNullOrWhiteSpace(other.ParameterKey))
        {
            return string.Equals(ParameterKey, other.ParameterKey, StringComparison.Ordinal);
        }

        return Boolean == other.Boolean &&
            Integer == other.Integer &&
            UnsignedInteger == other.UnsignedInteger &&
            Number.Equals(other.Number) &&
            Decimal == other.Decimal &&
            string.Equals(Text, other.Text, StringComparison.Ordinal) &&
            Guid == other.Guid;
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        AppendTo(builder);
        return builder.ToString();
    }

    public void AppendTo(StringBuilder builder)
    {
        if (!_hasValue)
        {
            builder.Append("missing");
            return;
        }

        builder.Append('{').Append((int)Kind).Append(':');
        if (!string.IsNullOrWhiteSpace(ParameterKey))
        {
            builder.Append("param:");
            FilterKeyText.AppendText(builder, ParameterKey);
            builder.Append('}');
            return;
        }

        AppendLiteral(builder);
        builder.Append('}');
    }

    private void AppendLiteral(StringBuilder builder)
    {
        switch (Kind)
        {
            case FilterValueKind.Boolean:
                builder.Append(Boolean ? '1' : '0');
                break;
            case FilterValueKind.Integer:
                builder.Append(Integer.ToString(CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.UnsignedInteger:
                builder.Append(UnsignedInteger.ToString(CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.Number:
                builder.Append(Number == 0D
                    ? "0"
                    : Number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case FilterValueKind.Decimal:
                builder.Append(Decimal.ToString("G29", CultureInfo.InvariantCulture));
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

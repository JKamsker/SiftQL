using System.Globalization;

namespace SiftQL.Expressions;

internal static class FilterValueIdentity
{
    public static bool Equals(FilterValue left, FilterValue right)
    {
        if (left.Kind != right.Kind)
            return false;

        return left.Kind switch
        {
            FilterValueKind.Null => true,
            FilterValueKind.Boolean => left.Boolean == right.Boolean,
            FilterValueKind.Integer => left.Integer == right.Integer,
            FilterValueKind.Number => BitConverter.DoubleToInt64Bits(left.Number) ==
                BitConverter.DoubleToInt64Bits(right.Number),
            FilterValueKind.String => string.Equals(left.String, right.String, StringComparison.Ordinal),
            FilterValueKind.Guid => left.Guid == right.Guid,
            FilterValueKind.UnsignedInteger => left.UnsignedInteger == right.UnsignedInteger,
            FilterValueKind.Decimal => left.Decimal == right.Decimal,
            FilterValueKind.Timestamp => TimestampText(left.Timestamp) == TimestampText(right.Timestamp),
            _ => false,
        };
    }

    public static string TimestampText(DateTimeOffset value) =>
        value.ToString("o", CultureInfo.InvariantCulture);
}

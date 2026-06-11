using System.Globalization;

namespace SiftQL.Expressions;

internal static class FilterValueIdentity
{
    public static bool Equals(FilterValue left, FilterValue right)
    {
        if (left.Kind != right.Kind)
            return false;

        return left.Kind == FilterValueKind.Timestamp
            ? TimestampText(left.Timestamp) == TimestampText(right.Timestamp)
            : EqualityComparer<FilterValue>.Default.Equals(left, right);
    }

    public static string TimestampText(DateTimeOffset value) =>
        value.ToString("o", CultureInfo.InvariantCulture);
}

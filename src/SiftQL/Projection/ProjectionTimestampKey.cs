using System.Globalization;

namespace SiftQL.Projection;

internal static class ProjectionTimestampKey
{
    public static string From(DateTimeOffset value) =>
        value.ToString("o", CultureInfo.InvariantCulture);

    public static bool Equals(DateTimeOffset left, DateTimeOffset right) =>
        string.Equals(From(left), From(right), StringComparison.Ordinal);
}

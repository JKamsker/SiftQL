using System.Globalization;
using System.Text;

namespace SiftQL.Values;

internal static class FilterKeyText
{
    public static void AppendText(StringBuilder builder, string? value)
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
}

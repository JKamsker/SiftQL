using SiftQL;
using SiftQL.Expressions;
using SiftQL.Values;

namespace SiftQL.Compiler;

internal static class FilterNumericInValues
{
    public static T[] Integral<T>(
        FilterValue[] values,
        decimal min,
        decimal max,
        Func<decimal, T> convert)
    {
        var seen = new HashSet<T>();
        var items = new List<T>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            if (!TryIntegral(values[i], min, max, out decimal number))
                continue;

            T item = convert(number);
            if (seen.Add(item))
                items.Add(item);
        }

        return items.ToArray();
    }

    public static decimal[] Decimal(FilterValue[] values)
    {
        var seen = new HashSet<decimal>();
        var items = new List<decimal>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            if (!TryDecimal(values[i], out decimal number) || !seen.Add(number))
                continue;

            items.Add(number);
        }

        return items.ToArray();
    }

    public static bool TryIntegral(
        FilterValue value,
        decimal min,
        decimal max,
        out decimal number)
    {
        if (!TryDecimal(value, out number) ||
            decimal.Truncate(number) != number ||
            number < min ||
            number > max)
        {
            number = 0;
            return false;
        }

        return true;
    }

    public static bool TryDecimal(FilterValue value, out decimal number)
    {
        number = 0;
        return value.Kind switch
        {
            FilterValueKind.Integer => Set(value.Integer, out number),
            FilterValueKind.UnsignedInteger => Set(value.UnsignedInteger, out number),
            FilterValueKind.Number => TryDoubleDecimal(value.Number, out number),
            FilterValueKind.Decimal => Set(value.Decimal, out number),
            _ => false,
        };
    }

    private static bool TryDoubleDecimal(double value, out decimal number) =>
        FilterNumeric.TryDoubleToDecimal(value, out number);

    private static bool Set(decimal value, out decimal number)
    {
        number = value;
        return true;
    }

}

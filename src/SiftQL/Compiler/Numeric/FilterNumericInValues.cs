using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Compiler;

internal static class FilterNumericInValues
{
    public static T[] Integral<T>(
        FilterValue[] values,
        decimal min,
        decimal max,
        Func<decimal, T> convert)
    {
        var items = new List<T>(values.Length);
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < values.Length; i++)
        {
            if (!TryIntegral(values[i], min, max, out decimal number))
                continue;

            T item = convert(number);
            if (!Contains(items, item, comparer))
                items.Add(item);
        }

        return items.ToArray();
    }

    public static decimal[] Decimal(FilterValue[] values)
    {
        var items = new List<decimal>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            if (!TryDecimal(values[i], out decimal number) || items.Contains(number))
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
            _ => false,
        };
    }

    private static bool TryDoubleDecimal(double value, out decimal number)
    {
        number = 0;
        if (double.IsNaN(value) || double.IsInfinity(value))
            return false;

        try
        {
            number = (decimal)value;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool Set(decimal value, out decimal number)
    {
        number = value;
        return true;
    }

    private static bool Contains<T>(
        IReadOnlyList<T> items,
        T candidate,
        EqualityComparer<T> comparer)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (comparer.Equals(items[i], candidate))
                return true;
        }

        return false;
    }
}

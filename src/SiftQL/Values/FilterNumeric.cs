using System.Globalization;
using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Values;

internal static class FilterNumeric
{
    public static bool IsNumeric(Type type) =>
        type == typeof(byte) ||
        type == typeof(sbyte) ||
        type == typeof(short) ||
        type == typeof(ushort) ||
        type == typeof(int) ||
        type == typeof(uint) ||
        type == typeof(long) ||
        type == typeof(ulong) ||
        type == typeof(float) ||
        type == typeof(double) ||
        type == typeof(decimal);

    public static bool IsSignedIntegral(Type type) =>
        type == typeof(sbyte) ||
        type == typeof(short) ||
        type == typeof(int) ||
        type == typeof(long);

    public static bool IsUnsignedIntegral(Type type) =>
        type == typeof(byte) ||
        type == typeof(ushort) ||
        type == typeof(uint) ||
        type == typeof(ulong);

    public static bool IsExactNumeric(Type type) =>
        IsSignedIntegral(type) ||
        IsUnsignedIntegral(type) ||
        type == typeof(decimal);

    public static bool TryDoubleToDecimal(double value, out decimal number)
    {
        number = 0;
        if (double.IsNaN(value) || double.IsInfinity(value))
            return false;
        if (value == Math.Truncate(value) &&
            TryIntegralDoubleToDecimal(value, out number))
        {
            return true;
        }

        try
        {
            number = (decimal)value;
            if (number == 0m && value != 0D)
                return false;

            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryIntegralDoubleToDecimal(double value, out decimal number)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return decimal.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number) &&
            decimal.Truncate(number) == number;
    }

    public static bool TryNumberDecimal(FilterValue value, out decimal number)
    {
        number = 0;
        if (value.Kind == FilterValueKind.Integer)
        {
            number = value.Integer;
            return true;
        }

        if (value.Kind == FilterValueKind.UnsignedInteger)
        {
            number = value.UnsignedInteger;
            return true;
        }

        if (value.Kind == FilterValueKind.Decimal)
        {
            number = value.Decimal;
            return true;
        }

        return value.Kind == FilterValueKind.Number &&
            TryDoubleToDecimal(value.Number, out number);
    }

    public static bool TryDoubleToInt64(double value, out long integer)
    {
        integer = 0;
        if (!TryDoubleToDecimal(value, out decimal number) ||
            decimal.Truncate(number) != number ||
            number < long.MinValue ||
            number > long.MaxValue)
        {
            return false;
        }

        integer = (long)number;
        return true;
    }

    public static bool TryDoubleToUInt64(double value, out ulong integer)
    {
        integer = 0;
        if (!TryDoubleToDecimal(value, out decimal number) ||
            decimal.Truncate(number) != number ||
            number < 0 ||
            number > ulong.MaxValue)
        {
            return false;
        }

        integer = (ulong)number;
        return true;
    }

    public static bool TryExactDecimal(object? value, out decimal number)
    {
        switch (value)
        {
            case byte item: number = item; return true;
            case sbyte item: number = item; return true;
            case short item: number = item; return true;
            case ushort item: number = item; return true;
            case int item: number = item; return true;
            case uint item: number = item; return true;
            case long item: number = item; return true;
            case ulong item: number = item; return true;
            case decimal item: number = item; return true;
            default: number = 0; return false;
        }
    }
}

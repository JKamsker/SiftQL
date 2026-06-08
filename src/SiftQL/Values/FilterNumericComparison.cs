using SiftQL;

namespace SiftQL.Values;

internal static class FilterNumericComparison
{
    private const double LongMaxExclusive = 9_223_372_036_854_775_808D;
    private const double ULongMaxExclusive = 18_446_744_073_709_551_616D;

    public static bool AreIntegerEqual(object actual, long expected)
    {
        if (TryInteger(actual, out long signed, out ulong unsigned, out bool isUnsigned))
            return isUnsigned
                ? expected >= 0 && unsigned == (ulong)expected
                : signed == expected;
        if (TryCompareFloatingToSigned(actual, expected, out int comparison))
            return comparison == 0;
        return actual is decimal decimalValue
            ? decimalValue == expected
            : TryNumber(actual, out double number) && number == expected;
    }

    public static bool AreUnsignedIntegerEqual(object actual, ulong expected)
    {
        if (TryInteger(actual, out long signed, out ulong unsigned, out bool isUnsigned))
            return isUnsigned ? unsigned == expected : signed >= 0 && (ulong)signed == expected;
        if (TryCompareFloatingToUnsigned(actual, expected, out int comparison))
            return comparison == 0;
        return actual is decimal decimalValue
            ? decimalValue >= 0 && decimalValue == expected
            : TryNumber(actual, out double number) && number == expected;
    }

    public static bool AreNumberEqual(object actual, double expected)
    {
        if (FilterNumeric.TryExactDecimal(actual, out decimal actualDecimal))
        {
            return FilterNumeric.TryDoubleToDecimal(expected, out decimal expectedDecimal) &&
                actualDecimal == expectedDecimal;
        }

        return TryNumber(actual, out double number) && number == expected;
    }

    public static bool AreDecimalEqual(object actual, decimal expected)
    {
        if (FilterNumeric.TryExactDecimal(actual, out decimal actualDecimal))
            return actualDecimal == expected;

        return TryNumber(actual, out double number) &&
            FilterNumeric.TryDoubleToDecimal(number, out decimal actualNumber) &&
            actualNumber == expected;
    }

    public static bool TryCompareInteger(object? actual, long expected, out int comparison)
    {
        if (TryInteger(actual, out long signed, out ulong unsigned, out bool isUnsigned))
        {
            comparison = isUnsigned
                ? CompareUnsignedToSigned(unsigned, expected)
                : signed.CompareTo(expected);
            return true;
        }

        if (actual is decimal decimalValue)
        {
            comparison = decimalValue.CompareTo(expected);
            return true;
        }

        if (TryCompareFloatingToSigned(actual, expected, out comparison))
            return true;

        comparison = 0;
        return false;
    }

    public static bool TryCompareUnsignedInteger(object? actual, ulong expected, out int comparison)
    {
        if (TryInteger(actual, out long signed, out ulong unsigned, out bool isUnsigned))
        {
            comparison = isUnsigned
                ? unsigned.CompareTo(expected)
                : CompareSignedToUnsigned(signed, expected);
            return true;
        }

        if (actual is decimal decimalValue)
        {
            comparison = decimalValue.CompareTo(expected);
            return true;
        }

        if (TryCompareFloatingToUnsigned(actual, expected, out comparison))
            return true;

        comparison = 0;
        return false;
    }

    public static bool TryCompareExactNumber(object? actual, double expected, out int comparison)
    {
        comparison = 0;
        if (!FilterNumeric.TryExactDecimal(actual, out decimal actualDecimal) ||
            !FilterNumeric.TryDoubleToDecimal(expected, out decimal expectedDecimal))
        {
            return false;
        }

        comparison = actualDecimal.CompareTo(expectedDecimal);
        return true;
    }

    public static bool TryCompareDecimal(object? actual, decimal expected, out int comparison)
    {
        comparison = 0;
        if (FilterNumeric.TryExactDecimal(actual, out decimal actualDecimal))
        {
            comparison = actualDecimal.CompareTo(expected);
            return true;
        }

        if (!TryNumber(actual, out double actualNumber) ||
            !FilterNumeric.TryDoubleToDecimal(actualNumber, out decimal converted))
        {
            return false;
        }

        comparison = converted.CompareTo(expected);
        return true;
    }

    public static bool TryNumber(object? value, out double number)
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
            case float item: number = item; return true;
            case double item: number = item; return true;
            case decimal item: number = (double)item; return true;
            default: number = 0; return false;
        }
    }

    private static bool TryInteger(
        object? value,
        out long signed,
        out ulong unsigned,
        out bool isUnsigned)
    {
        signed = 0;
        unsigned = 0;
        isUnsigned = false;
        switch (value)
        {
            case byte item: signed = item; return true;
            case sbyte item: signed = item; return true;
            case short item: signed = item; return true;
            case ushort item: signed = item; return true;
            case int item: signed = item; return true;
            case uint item: signed = item; return true;
            case long item: signed = item; return true;
            case ulong item:
                if (item <= long.MaxValue)
                    signed = (long)item;
                else
                {
                    unsigned = item;
                    isUnsigned = true;
                }

                return true;
            default:
                return false;
        }
    }

    private static int CompareUnsignedToSigned(ulong actual, long expected) =>
        expected < 0 ? 1 : actual.CompareTo((ulong)expected);

    private static int CompareSignedToUnsigned(long actual, ulong expected) =>
        actual < 0 ? -1 : ((ulong)actual).CompareTo(expected);

    private static bool TryCompareFloatingToSigned(
        object? actual,
        long expected,
        out int comparison) =>
        actual switch
        {
            float item => TryCompareDoubleToSigned(item, expected, out comparison),
            double item => TryCompareDoubleToSigned(item, expected, out comparison),
            _ => False(out comparison),
        };

    private static bool TryCompareFloatingToUnsigned(
        object? actual,
        ulong expected,
        out int comparison) =>
        actual switch
        {
            float item => TryCompareDoubleToUnsigned(item, expected, out comparison),
            double item => TryCompareDoubleToUnsigned(item, expected, out comparison),
            _ => False(out comparison),
        };

    private static bool TryCompareDoubleToSigned(
        double actual,
        long expected,
        out int comparison)
    {
        if (double.IsNaN(actual))
            return False(out comparison);
        if (actual < long.MinValue)
            return Compared(-1, out comparison);
        if (actual >= LongMaxExclusive)
            return Compared(1, out comparison);

        double roundedExpected = expected;
        if (actual < roundedExpected)
            return Compared(-1, out comparison);
        if (actual > roundedExpected)
            return Compared(1, out comparison);

        return Compared(((long)actual).CompareTo(expected), out comparison);
    }

    private static bool TryCompareDoubleToUnsigned(
        double actual,
        ulong expected,
        out int comparison)
    {
        if (double.IsNaN(actual))
            return False(out comparison);
        if (actual < 0D)
            return Compared(-1, out comparison);
        if (actual >= ULongMaxExclusive)
            return Compared(1, out comparison);

        double roundedExpected = expected;
        if (actual < roundedExpected)
            return Compared(-1, out comparison);
        if (actual > roundedExpected)
            return Compared(1, out comparison);

        return Compared(((ulong)actual).CompareTo(expected), out comparison);
    }

    private static bool Compared(int result, out int comparison)
    {
        comparison = result;
        return true;
    }

    private static bool False(out int comparison)
    {
        comparison = 0;
        return false;
    }
}

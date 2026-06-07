using System.Diagnostics.CodeAnalysis;

namespace SiftQL.Schema;

public static class FilterArrayContains
{
    private const int MaxRuntimeArrayItems = 256;

    public static bool ContainsBoolean(bool[]? items, bool expected)
    {
        if (!CanSearch(items))
            return false;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == expected)
                return true;
        }

        return false;
    }

    public static bool ContainsByte(byte[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsByteValue(byte[]? items, byte expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsSByte(sbyte[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsSByteValue(sbyte[]? items, sbyte expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsInt16(short[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsInt16Value(short[]? items, short expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsUInt16(ushort[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsUInt16Value(ushort[]? items, ushort expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsInt32(int[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        if (!Values.FilterNumeric.TryDoubleToInt64(expected, out long intExpected) ||
            intExpected < int.MinValue || intExpected > int.MaxValue)
            return false;
        int target = (int)intExpected;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == target) return true;
        return false;
    }

    public static bool ContainsInt32Value(int[]? items, int expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsUInt32(uint[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        if (!Values.FilterNumeric.TryDoubleToUInt64(expected, out ulong intExpected) ||
            intExpected > uint.MaxValue)
            return false;
        uint target = (uint)intExpected;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == target) return true;
        return false;
    }

    public static bool ContainsUInt32Value(uint[]? items, uint expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsInt64(long[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        if (!Values.FilterNumeric.TryDoubleToInt64(expected, out long intExpected)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == intExpected) return true;
        return false;
    }

    public static bool ContainsInt64Value(long[]? items, long expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsUInt64(ulong[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        if (!Values.FilterNumeric.TryDoubleToUInt64(expected, out ulong intExpected)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == intExpected) return true;
        return false;
    }

    public static bool ContainsUInt64Value(ulong[]? items, ulong expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsSingle(float[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        float expectedFloat = (float)expected;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expectedFloat) return true;
        return false;
    }

    public static bool ContainsDouble(double[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsDecimal(decimal[]? items, double expected)
    {
        if (!CanSearch(items)) return false;
        try
        {
            decimal expectedDecimal = (decimal)expected;
            for (int i = 0; i < items.Length; i++)
                if (items[i] == expectedDecimal) return true;
        }
        catch (OverflowException)
        {
            return false;
        }
        return false;
    }

    public static bool ContainsDecimalValue(decimal[]? items, decimal expected)
    {
        if (!CanSearch(items)) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsString(string?[]? items, string? expected)
    {
        if (!CanSearch(items))
            return false;

        for (int i = 0; i < items.Length; i++)
        {
            if (string.Equals(items[i], expected, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static bool ContainsGuid(Guid[]? items, Guid expected)
    {
        if (!CanSearch(items))
            return false;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == expected)
                return true;
        }

        return false;
    }

    private static bool CanSearch([NotNullWhen(true)] Array? items)
    {
        if (items is null)
            return false;
        if (items.Length > MaxRuntimeArrayItems)
            throw new InvalidOperationException(
                $"Runtime array filters support at most {MaxRuntimeArrayItems} items.");

        return true;
    }
}

namespace SiftQL.Schema;

public static class FilterArrayContains
{
    private const int MaxRuntimeArrayItems = 256;

    public static bool ContainsBoolean(bool[]? items, bool expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems)
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
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsByteValue(byte[]? items, byte expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsSByte(sbyte[]? items, double expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsSByteValue(sbyte[]? items, sbyte expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsInt16(short[]? items, double expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsInt16Value(short[]? items, short expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsUInt16(ushort[]? items, double expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsUInt16Value(ushort[]? items, ushort expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsInt32(int[]? items, double expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsInt32Value(int[]? items, int expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsUInt32(uint[]? items, double expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsUInt32Value(uint[]? items, uint expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsInt64(long[]? items, double expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsInt64Value(long[]? items, long expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsUInt64(ulong[]? items, double expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsUInt64Value(ulong[]? items, ulong expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsSingle(float[]? items, double expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsDouble(double[]? items, double expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsDecimal(decimal[]? items, double expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if ((double)items[i] == expected) return true;
        return false;
    }

    public static bool ContainsDecimalValue(decimal[]? items, decimal expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems) return false;
        for (int i = 0; i < items.Length; i++)
            if (items[i] == expected) return true;
        return false;
    }

    public static bool ContainsString(string?[]? items, string? expected)
    {
        if (items is null || items.Length > MaxRuntimeArrayItems)
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
        if (items is null || items.Length > MaxRuntimeArrayItems)
            return false;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == expected)
                return true;
        }

        return false;
    }
}

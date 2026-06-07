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

    public static bool ContainsSByte(sbyte[]? items, double expected)
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

    public static bool ContainsUInt16(ushort[]? items, double expected)
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

    public static bool ContainsUInt32(uint[]? items, double expected)
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

    public static bool ContainsUInt64(ulong[]? items, double expected)
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

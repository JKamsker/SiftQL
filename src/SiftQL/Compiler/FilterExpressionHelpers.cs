namespace SiftQL.Compiler;

internal static class FilterExpressionHelpers
{
    public static bool NumberIn(double actual, double[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (actual == expected[i])
                return true;
        }

        return false;
    }

    public static bool StringIn(string? actual, string?[] expected, bool hasNull)
    {
        if (actual is null)
            return hasNull;

        for (int i = 0; i < expected.Length; i++)
        {
            if (string.Equals(actual, expected[i], StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static bool GuidIn(Guid actual, Guid[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (actual == expected[i])
                return true;
        }

        return false;
    }

    public static bool EnumIn(long actual, long[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (actual == expected[i])
                return true;
        }

        return false;
    }
}

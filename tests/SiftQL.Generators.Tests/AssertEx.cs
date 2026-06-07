namespace SiftQL.Generators.Tests;

internal static class AssertEx
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}. Expected: {expected}. Actual: {actual}.");
    }

    public static void Contains(string expected, string actual, string message)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(message + " Missing: " + expected);
    }

    public static void DoesNotContain(string unexpected, string actual, string message)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
            throw new InvalidOperationException(message + " Unexpected: " + unexpected);
    }
}

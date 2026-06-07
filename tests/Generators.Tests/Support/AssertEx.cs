namespace SiftQL.Generators.Tests;

internal static class AssertEx
{
    public static void True(bool condition, string message)
    {
        Xunit.Assert.True(condition, message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        Xunit.Assert.True(
            EqualityComparer<T>.Default.Equals(expected, actual),
            $"{message}. Expected: {expected}. Actual: {actual}.");
    }

    public static void NotEqual<T>(T unexpected, T actual, string message)
    {
        Xunit.Assert.False(
            EqualityComparer<T>.Default.Equals(unexpected, actual),
            $"{message}. Unexpected: {unexpected}.");
    }

    public static void Contains(string expected, string actual, string message)
    {
        Xunit.Assert.True(
            actual.Contains(expected, StringComparison.Ordinal),
            message + " Missing: " + expected);
    }

    public static void DoesNotContain(string unexpected, string actual, string message)
    {
        Xunit.Assert.False(
            actual.Contains(unexpected, StringComparison.Ordinal),
            message + " Unexpected: " + unexpected);
    }
}

namespace SiftQL.Expressions;

public static class EventProjectionConstantIntrinsics
{
    public const string Value = "siftql.constant";
    public const string ArgumentName = "value";

    public static bool IsConstant(string intrinsic) =>
        string.Equals(intrinsic, Value, StringComparison.Ordinal);
}

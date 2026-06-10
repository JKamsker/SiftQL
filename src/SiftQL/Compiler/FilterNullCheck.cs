using SiftQL.Expressions;
using SiftQL.Schema;

namespace SiftQL.Compiler;

// A Compare(field, Equal|NotEqual, null) against a non-scalar (object or array)
// field is a member presence check, not a scalar comparison. It lowers to
// Exists / Not(Exists) instead of failing the "not scalar" guard, so authors can
// write x.Member == null / x.Member != null next to the member-read form that
// already null-propagates. Scalar-field null comparisons keep their normal
// Compare semantics.
internal static class FilterNullCheck
{
    public static bool IsPresenceCheck(FilterField field, FilterExpression expression) =>
        field.Kind != FilterFieldKind.Scalar &&
        expression.Operator is FilterOperator.Equal or FilterOperator.NotEqual &&
        expression.Value is { Kind: FilterValueKind.Null };

    // field != null matches present (non-null) subjects; field == null matches absent ones.
    public static bool MatchesPresent(FilterExpression expression) =>
        expression.Operator == FilterOperator.NotEqual;
}

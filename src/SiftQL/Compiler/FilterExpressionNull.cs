using System.Linq.Expressions;

namespace SiftQL.Compiler;

internal static class FilterExpressionNull
{
    public static Expression IsNull(Expression actual)
    {
        Type type = actual.Type;
        if (Nullable.GetUnderlyingType(type) is not null)
            return Expression.Not(Expression.Property(actual, nameof(Nullable<int>.HasValue)));
        return type.IsValueType
            ? Expression.Constant(false)
            : Expression.Equal(actual, Expression.Constant(null, type));
    }
}

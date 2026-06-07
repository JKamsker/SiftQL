using SiftQL;
using SiftQL.Expressions;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Index;

public static class FilterIndexExtractor
{
    public static FilterIndexKey? Extract(
        Type subjectType,
        FilterExpression? expression,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        var schema = FilterSchema.For(subjectType);
        return Extract(schema, expression ?? FilterExpression.Any, errorFactory);
    }

    public static FilterIndexKey? Extract(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(expression);
        return FindExactScalar(schema, expression, errorFactory);
    }

    private static FilterIndexKey? FindExactScalar(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        if (expression.Kind == FilterExpressionKind.Compare)
            return BuildCompareKey(schema, expression, errorFactory);

        if (expression.Kind == FilterExpressionKind.And)
        {
            for (int i = 0; i < expression.Children.Length; i++)
            {
                FilterIndexKey? key = FindExactScalar(schema, expression.Children[i], errorFactory);
                if (key is not null)
                    return key;
            }
        }

        return null;
    }

    private static FilterIndexKey? BuildCompareKey(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        if (expression.Operator != FilterOperator.Equal || expression.Value is null)
            return null;
        if (!schema.TryGetField(expression.Field, out FilterField? field) ||
            field.Kind != FilterFieldKind.Scalar)
        {
            return null;
        }

        FilterValues.ValidateComparison(field, expression.Operator, expression.Value, errorFactory);
        return TryCreateFieldValue(field, expression.Value, out FilterIndexValue value)
            ? new FilterIndexKey(field.Name, value)
            : null;
    }

    private static bool TryCreateFieldValue(
        FilterField field,
        FilterValue value,
        out FilterIndexValue key)
    {
        key = default;
        Type type = Nullable.GetUnderlyingType(field.ValueType) ?? field.ValueType;

        if (type.IsEnum)
            return TryCreateEnumValue(type, value, out key);

        if (type == typeof(ulong) && value.Kind == FilterValueKind.Integer)
        {
            if (value.Integer < 0)
                return false;
            key = FilterIndexValue.ForInteger(value.Integer);
            return true;
        }

        if (type == typeof(ulong) && value.Kind == FilterValueKind.UnsignedInteger)
        {
            key = FilterIndexValue.ForUnsignedInteger(value.UnsignedInteger);
            return true;
        }

        if (IsFloating(type) && value.Kind == FilterValueKind.Integer)
        {
            key = FilterIndexValue.ForNumber(value.Integer);
            return true;
        }

        if (IsFloating(type) && value.Kind == FilterValueKind.UnsignedInteger)
        {
            if (type == typeof(decimal))
                return false;
            key = FilterIndexValue.ForNumber(value.UnsignedInteger);
            return true;
        }

        if (value.Kind == FilterValueKind.Number &&
            (FilterNumeric.IsSignedIntegral(type) || FilterNumeric.IsUnsignedIntegral(type)))
        {
            if (!FilterNumeric.TryDoubleToInt64(value.Number, out long integer) ||
                (FilterNumeric.IsUnsignedIntegral(type) && integer < 0))
            {
                return false;
            }

            key = FilterIndexValue.ForInteger(integer);
            return true;
        }

        return FilterIndexValue.TryCreate(value, out key);
    }

    private static bool TryCreateEnumValue(
        Type enumType,
        FilterValue value,
        out FilterIndexValue key)
    {
        key = default;
        if (value.Kind == FilterValueKind.Integer)
        {
            key = FilterIndexValue.ForEnum(value.Integer);
            return true;
        }

        if (value.Kind != FilterValueKind.String ||
            string.IsNullOrWhiteSpace(value.String) ||
            !Enum.TryParse(enumType, value.String, ignoreCase: false, out object? parsed))
        {
            return false;
        }

        key = FilterIndexValue.ForEnum(Convert.ToInt64(parsed));
        return true;
    }

    private static bool IsFloating(Type type) =>
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);
}

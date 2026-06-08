using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Index;

public static class FilterIndexExtractor
{
    private const double LongMaxExclusive = 9_223_372_036_854_775_808D;
    private const double ULongMaxExclusive = 18_446_744_073_709_551_616D;

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
        FilterExpressionShapeValidator.Validate(expression, errorFactory);
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
            FilterIndexKey? best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < expression.Children.Length; i++)
            {
                FilterIndexKey? key = FindExactScalar(schema, expression.Children[i], errorFactory);
                if (key is null)
                    continue;

                int score = SelectivityScore(key.Field);
                if (score < bestScore)
                {
                    best = key;
                    bestScore = score;
                }
            }

            return best;
        }

        return null;
    }

    private static int SelectivityScore(string field) =>
        field switch
        {
            "subjectType" or "subjectName" => 100,
            _ when field.EndsWith("Id", StringComparison.Ordinal) ||
                field.EndsWith(".Id", StringComparison.Ordinal) => 0,
            _ when field.Contains("Character", StringComparison.Ordinal) ||
                field.Contains("Session", StringComparison.Ordinal) ||
                field.Contains("Item", StringComparison.Ordinal) ||
                field.Contains("Skill", StringComparison.Ordinal) => 5,
            _ => 50,
        };

    private static FilterIndexKey? BuildCompareKey(
        FilterSchema schema,
        FilterExpression expression,
        Func<string, Exception>? errorFactory)
    {
        if (expression.Value is null)
            return null;
        if (!schema.TryGetField(expression.Field, out FilterField? field) ||
            field.Kind != FilterFieldKind.Scalar)
        {
            return null;
        }

        FilterValues.ValidateComparison(field, expression.Operator, expression.Value, errorFactory);
        if (expression.Operator != FilterOperator.Equal)
            return null;

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
        if (field.ValueType == typeof(ProjectedEventValue))
            return false;

        Type type = Nullable.GetUnderlyingType(field.ValueType) ?? field.ValueType;
        if (type == typeof(decimal) || value.Kind == FilterValueKind.Decimal)
            return false;

        if (type.IsEnum)
            return TryCreateEnumValue(type, value, out key);

        if (type == typeof(ulong) && value.Kind == FilterValueKind.Integer)
        {
            if (value.Integer < 0)
                return false;
            key = FilterIndexValue.ForInteger(value.Integer);
            return true;
        }

        if (value.Kind == FilterValueKind.UnsignedInteger &&
            (FilterNumeric.IsUnsignedIntegral(type) || FilterNumeric.IsSignedIntegral(type)))
        {
            if (value.UnsignedInteger <= long.MaxValue)
            {
                key = FilterIndexValue.ForInteger((long)value.UnsignedInteger);
                return true;
            }

            if (type == typeof(ulong))
            {
                key = FilterIndexValue.ForUnsignedInteger(value.UnsignedInteger);
                return true;
            }

            return false;
        }

        if (IsFloating(type) && value.Kind == FilterValueKind.Integer)
        {
            return TryCreateFloatingIntegerValue(value.Integer, out key);
        }

        if (IsFloating(type) && value.Kind == FilterValueKind.UnsignedInteger)
        {
            return TryCreateFloatingUnsignedIntegerValue(value.UnsignedInteger, out key);
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
            if (Enum.GetUnderlyingType(enumType) == typeof(ulong) && value.Integer < 0)
                return false;

            key = FilterIndexValue.ForEnum(value.Integer);
            return true;
        }

        if (value.Kind != FilterValueKind.String ||
            string.IsNullOrWhiteSpace(value.String) ||
            !Enum.TryParse(enumType, value.String, ignoreCase: false, out object? parsed))
        {
            return false;
        }

        if (!TryConvertEnumToInt64(parsed, out long enumValue))
            return false;

        key = FilterIndexValue.ForEnum(enumValue);
        return true;
    }

    private static bool TryConvertEnumToInt64(object value, out long result)
    {
        try
        {
            result = Convert.ToInt64(value);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private static bool IsFloating(Type type) =>
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static bool TryCreateFloatingIntegerValue(
        long value,
        out FilterIndexValue key)
    {
        key = default;
        double number = value;
        if (number >= LongMaxExclusive || (long)number != value)
            return false;

        key = FilterIndexValue.ForNumber(number);
        return true;
    }

    private static bool TryCreateFloatingUnsignedIntegerValue(
        ulong value,
        out FilterIndexValue key)
    {
        key = default;
        double number = value;
        if (number >= ULongMaxExclusive || (ulong)number != value)
            return false;

        key = FilterIndexValue.ForNumber(number);
        return true;
    }
}

using System.Collections;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;

namespace SiftQL.Values;

public static class FilterValues
{
    private const int MaxRuntimeArrayItems = 256;

    public static void ValidateComparison(
        FilterField field,
        FilterOperator op,
        FilterValue value,
        Func<string, Exception>? errorFactory = null)
    {
        ValidateValue(field, value, errorFactory);
        if (op is FilterOperator.Equal or FilterOperator.NotEqual)
            return;

        if (IsProjectedDynamic(field.ValueType) &&
            value.Kind is (FilterValueKind.Integer or
                FilterValueKind.UnsignedInteger or
                FilterValueKind.Number))
        {
            return;
        }

        if (!FilterNumeric.IsNumeric(field.ValueType))
        {
            throw Error(
                errorFactory,
                $"Filter field '{field.Name}' does not support ordered comparisons.");
        }
    }

    public static void ValidateValue(
        FilterField field,
        FilterValue value,
        Func<string, Exception>? errorFactory = null)
    {
        if (value.Kind == FilterValueKind.Null)
            return;
        if (IsProjectedDynamic(field.ValueType))
            return;

        Type type = Nullable.GetUnderlyingType(field.ValueType) ?? field.ValueType;
        bool valid =
            type.IsEnum && value.Kind is (FilterValueKind.String or FilterValueKind.Integer) ||
            type == typeof(bool) && value.Kind == FilterValueKind.Boolean ||
            FilterNumeric.IsNumeric(type) && value.Kind is (FilterValueKind.Integer or
                FilterValueKind.UnsignedInteger or
                FilterValueKind.Number) ||
            type == typeof(string) && value.Kind == FilterValueKind.String ||
            type == typeof(Guid) && value.Kind == FilterValueKind.Guid;

        if (!valid)
            throw Error(errorFactory, $"Filter value '{value.Kind}' is not compatible with field '{field.Name}'.");
    }

    public static bool Compare(object? actual, FilterValue expected, FilterOperator op) =>
        op switch
        {
            FilterOperator.Equal => AreEqual(actual, expected),
            FilterOperator.NotEqual => !AreEqual(actual, expected),
            FilterOperator.GreaterThan => TryCompareOrdered(actual, expected, out int comparison) && comparison > 0,
            FilterOperator.GreaterThanOrEqual => TryCompareOrdered(actual, expected, out int comparison) && comparison >= 0,
            FilterOperator.LessThan => TryCompareOrdered(actual, expected, out int comparison) && comparison < 0,
            FilterOperator.LessThanOrEqual => TryCompareOrdered(actual, expected, out int comparison) && comparison <= 0,
            _ => false,
        };

    public static bool In(object? actual, FilterValue[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (AreEqual(actual, expected[i]))
                return true;
        }

        return false;
    }

    public static bool Contains(object? actual, FilterValue expected)
    {
        if (actual is not IEnumerable enumerable || actual is string)
            return false;

        int seen = 0;
        foreach (object? item in enumerable)
        {
            if (++seen > MaxRuntimeArrayItems)
                return false;
            if (AreEqual(item, expected))
                return true;
        }

        return false;
    }

    private static bool AreEqual(object? actual, FilterValue expected)
    {
        if (actual is null || expected.Kind == FilterValueKind.Null)
            return actual is null && expected.Kind == FilterValueKind.Null;

        if (actual.GetType().IsEnum)
        {
            return expected.Kind switch
            {
                FilterValueKind.String =>
                    string.Equals(actual.ToString(), expected.String, StringComparison.Ordinal),
                FilterValueKind.Integer => Convert.ToInt64(actual) == expected.Integer,
                _ => false,
            };
        }

        return expected.Kind switch
        {
            FilterValueKind.Boolean => actual is bool item && item == expected.Boolean,
            FilterValueKind.Integer => FilterNumericComparison.AreIntegerEqual(actual, expected.Integer),
            FilterValueKind.UnsignedInteger =>
                FilterNumericComparison.AreUnsignedIntegerEqual(actual, expected.UnsignedInteger),
            FilterValueKind.Number => FilterNumericComparison.AreNumberEqual(actual, expected.Number),
            FilterValueKind.String => actual is string item &&
                string.Equals(item, expected.String, StringComparison.Ordinal),
            FilterValueKind.Guid => actual is Guid item && item == expected.Guid,
            _ => false,
        };
    }

    private static bool TryCompareOrdered(object? actual, FilterValue expected, out int comparison)
    {
        comparison = 0;
        if (actual is null || expected.Kind == FilterValueKind.Null)
            return false;

        if (expected.Kind == FilterValueKind.Integer &&
            FilterNumericComparison.TryCompareInteger(actual, expected.Integer, out comparison))
        {
            return true;
        }

        if (expected.Kind == FilterValueKind.UnsignedInteger &&
            FilterNumericComparison.TryCompareUnsignedInteger(actual, expected.UnsignedInteger, out comparison))
        {
            return true;
        }

        if (expected.Kind == FilterValueKind.Number &&
            FilterNumericComparison.TryCompareExactNumber(actual, expected.Number, out comparison))
        {
            return true;
        }

        if (!FilterNumericComparison.TryNumber(actual, out double actualNumber))
            return false;
        if (double.IsNaN(actualNumber))
            return false;

        double expectedNumber = expected.Kind switch
        {
            FilterValueKind.Integer => expected.Integer,
            FilterValueKind.UnsignedInteger => expected.UnsignedInteger,
            _ => expected.Number,
        };
        if (double.IsNaN(expectedNumber))
            return false;

        comparison = actualNumber.CompareTo(expectedNumber);
        return true;
    }

    private static bool IsProjectedDynamic(Type type) =>
        type == typeof(ProjectedEventValue);

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);
}

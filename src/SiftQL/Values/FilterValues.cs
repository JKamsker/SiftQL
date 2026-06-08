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
    private const string TooManyRuntimeArrayItemsMessage =
        "Runtime array filters support at most 256 items.";

    public static void ValidateComparison(
        FilterField field,
        FilterOperator op,
        FilterValue value,
        Func<string, Exception>? errorFactory = null)
    {
        ValidateValue(field, value, errorFactory);
        if (op == FilterOperator.StringContains)
        {
            if (value.Kind == FilterValueKind.String &&
                (field.ValueType == typeof(string) || IsProjectedDynamic(field.ValueType)))
            {
                return;
            }

            throw Error(errorFactory, $"Filter field '{field.Name}' does not support string contains.");
        }

        if (op is FilterOperator.Equal or FilterOperator.NotEqual)
            return;

        if (op is not FilterOperator.GreaterThan and
            not FilterOperator.GreaterThanOrEqual and
            not FilterOperator.LessThan and
            not FilterOperator.LessThanOrEqual)
        {
            throw Error(errorFactory, $"Filter operator '{op}' is not supported.");
        }

        if (IsProjectedDynamic(field.ValueType) &&
            value.Kind is (FilterValueKind.Integer or
                FilterValueKind.UnsignedInteger or
                FilterValueKind.Number or
                FilterValueKind.Decimal))
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
            type.IsEnum && value.Kind is (FilterValueKind.String or
                FilterValueKind.Integer or
                FilterValueKind.UnsignedInteger) ||
            type == typeof(bool) && value.Kind == FilterValueKind.Boolean ||
            FilterNumeric.IsNumeric(type) && value.Kind is (FilterValueKind.Integer or
                FilterValueKind.UnsignedInteger or
                FilterValueKind.Number or
                FilterValueKind.Decimal) ||
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
            FilterOperator.StringContains => ContainsString(actual, expected),
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
        if (actual is ICollection collection && collection.Count > MaxRuntimeArrayItems)
            throw TooManyRuntimeArrayItems();

        bool matched = false;
        try
        {
            int seen = 0;
            foreach (object? item in enumerable)
            {
                if (actual is not ICollection && ++seen > MaxRuntimeArrayItems)
                    throw TooManyRuntimeArrayItems();
                if (!matched && AreEqual(item, expected))
                    matched = true;
            }
        }
        catch (Exception ex) when (matched && !IsTooManyRuntimeArrayItems(ex))
        {
            return true;
        }

        return matched;
    }

    private static bool ContainsString(object? actual, FilterValue expected) =>
        actual is string text &&
        expected.Kind == FilterValueKind.String &&
        expected.String is string substring &&
        text.Contains(substring, StringComparison.Ordinal);

    private static InvalidOperationException TooManyRuntimeArrayItems() =>
        new(TooManyRuntimeArrayItemsMessage);

    private static bool IsTooManyRuntimeArrayItems(Exception ex) =>
        ex is InvalidOperationException { Message: TooManyRuntimeArrayItemsMessage };

    private static bool AreEqual(object? actual, FilterValue expected)
    {
        if (actual is null || expected.Kind == FilterValueKind.Null)
            return actual is null && expected.Kind == FilterValueKind.Null;

        if (actual.GetType().IsEnum)
        {
            return expected.Kind switch
            {
                FilterValueKind.String => IsEnumStringEqual(actual, expected.String),
                FilterValueKind.Integer => IsEnumIntegerEqual(actual, expected.Integer),
                FilterValueKind.UnsignedInteger =>
                    IsEnumUnsignedIntegerEqual(actual, expected.UnsignedInteger),
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
            FilterValueKind.Decimal => FilterNumericComparison.AreDecimalEqual(actual, expected.Decimal),
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

        if (expected.Kind == FilterValueKind.Decimal &&
            FilterNumericComparison.TryCompareDecimal(actual, expected.Decimal, out comparison))
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
            FilterValueKind.Number => expected.Number,
            FilterValueKind.Decimal => (double)expected.Decimal,
            _ => double.NaN,
        };
        if (double.IsNaN(expectedNumber))
            return false;

        comparison = actualNumber.CompareTo(expectedNumber);
        return true;
    }

    private static bool IsProjectedDynamic(Type type) =>
        type == typeof(ProjectedEventValue);

    private static bool IsEnumStringEqual(object actual, string? expected)
    {
        if (expected is null ||
            !Enum.TryParse(actual.GetType(), expected, ignoreCase: false, out object? parsed))
        {
            return false;
        }

        return actual.Equals(parsed);
    }

    private static bool IsEnumIntegerEqual(object actual, long expected)
    {
        Type underlying = Enum.GetUnderlyingType(actual.GetType());
        object value = Convert.ChangeType(
            actual,
            underlying,
            System.Globalization.CultureInfo.InvariantCulture);

        return Type.GetTypeCode(underlying) switch
        {
            TypeCode.Byte => expected >= 0 && (byte)value == (ulong)expected,
            TypeCode.UInt16 => expected >= 0 && (ushort)value == (ulong)expected,
            TypeCode.UInt32 => expected >= 0 && (uint)value == (ulong)expected,
            TypeCode.UInt64 => expected >= 0 && (ulong)value == (ulong)expected,
            TypeCode.SByte => (sbyte)value == expected,
            TypeCode.Int16 => (short)value == expected,
            TypeCode.Int32 => (int)value == expected,
            TypeCode.Int64 => (long)value == expected,
            _ => false,
        };
    }

    private static bool IsEnumUnsignedIntegerEqual(object actual, ulong expected)
    {
        Type underlying = Enum.GetUnderlyingType(actual.GetType());
        object value = Convert.ChangeType(
            actual,
            underlying,
            System.Globalization.CultureInfo.InvariantCulture);

        return Type.GetTypeCode(underlying) switch
        {
            TypeCode.Byte => (byte)value == expected,
            TypeCode.UInt16 => (ushort)value == expected,
            TypeCode.UInt32 => (uint)value == expected,
            TypeCode.UInt64 => (ulong)value == expected,
            TypeCode.SByte => (sbyte)value >= 0 && (ulong)(sbyte)value == expected,
            TypeCode.Int16 => (short)value >= 0 && (ulong)(short)value == expected,
            TypeCode.Int32 => (int)value >= 0 && (ulong)(int)value == expected,
            TypeCode.Int64 => (long)value >= 0 && (ulong)(long)value == expected,
            _ => false,
        };
    }

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);
}

using SiftQL.Expressions;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterValuesCompareTests
{
    [Fact] public void Num_Byte() => Assert.True(FilterValues.Compare((byte)1, FilterValue.From(1L), FilterOperator.Equal));

    [Fact] public void AreIntegerEqual_Byte_Match() =>
        Assert.True(FilterValues.Compare((byte)42, new FilterValue { Kind = FilterValueKind.Integer, Integer = 42L }, FilterOperator.Equal));

    [Fact] public void AreIntegerEqual_SByte_Match() =>
        Assert.True(FilterValues.Compare((sbyte)-5, new FilterValue { Kind = FilterValueKind.Integer, Integer = -5L }, FilterOperator.Equal));

    [Fact] public void AreIntegerEqual_Short_Match() =>
        Assert.True(FilterValues.Compare((short)1000, new FilterValue { Kind = FilterValueKind.Integer, Integer = 1000L }, FilterOperator.Equal));

    [Fact] public void AreIntegerEqual_UShort_Match() =>
        Assert.True(FilterValues.Compare((ushort)2000, new FilterValue { Kind = FilterValueKind.Integer, Integer = 2000L }, FilterOperator.Equal));

    [Fact] public void AreIntegerEqual_Int_Match() =>
        Assert.True(FilterValues.Compare(42, new FilterValue { Kind = FilterValueKind.Integer, Integer = 42L }, FilterOperator.Equal));

    [Fact] public void AreIntegerEqual_UInt_Match() =>
        Assert.True(FilterValues.Compare(42u, new FilterValue { Kind = FilterValueKind.Integer, Integer = 42L }, FilterOperator.Equal));

    [Fact]
    public void AreIntegerEqual_ULong_WithinLongMax_Match()
    {
        ulong val = 100UL;
        Assert.True(FilterValues.Compare(val, new FilterValue { Kind = FilterValueKind.Integer, Integer = 100L }, FilterOperator.Equal));
    }

    [Fact]
    public void AreIntegerEqual_ULong_BeyondLongMax_NegativeExpected_ReturnsFalse()
    {
        ulong big = (ulong)long.MaxValue + 2UL;
        Assert.False(FilterValues.Compare(big, new FilterValue { Kind = FilterValueKind.Integer, Integer = -1L }, FilterOperator.Equal));
    }

    [Fact] public void AreIntegerEqual_Decimal_Match() =>
        Assert.True(FilterValues.Compare(42m, new FilterValue { Kind = FilterValueKind.Integer, Integer = 42L }, FilterOperator.Equal));

    [Fact] public void AreIntegerEqual_Double_Match() =>
        Assert.True(FilterValues.Compare(42.0, new FilterValue { Kind = FilterValueKind.Integer, Integer = 42L }, FilterOperator.Equal));

    [Fact] public void AreIntegerEqual_String_ReturnsFalse() =>
        Assert.False(FilterValues.Compare("42", new FilterValue { Kind = FilterValueKind.Integer, Integer = 42L }, FilterOperator.Equal));

    [Fact] public void AreUnsignedIntegerEqual_NegativeSigned_ReturnsFalse() =>
        Assert.False(FilterValues.Compare(-1L, new FilterValue { Kind = FilterValueKind.UnsignedInteger, UnsignedInteger = 1UL }, FilterOperator.Equal));

    [Fact] public void AreUnsignedIntegerEqual_PositiveSigned_Match() =>
        Assert.True(FilterValues.Compare(42L, new FilterValue { Kind = FilterValueKind.UnsignedInteger, UnsignedInteger = 42UL }, FilterOperator.Equal));

    [Fact] public void AreUnsignedIntegerEqual_ULong_Match() =>
        Assert.True(FilterValues.Compare(5UL, new FilterValue { Kind = FilterValueKind.UnsignedInteger, UnsignedInteger = 5UL }, FilterOperator.Equal));

    [Fact] public void AreUnsignedIntegerEqual_Decimal_Match() =>
        Assert.True(FilterValues.Compare(5m, new FilterValue { Kind = FilterValueKind.UnsignedInteger, UnsignedInteger = 5UL }, FilterOperator.Equal));

    [Fact] public void AreUnsignedIntegerEqual_Double_Match() =>
        Assert.True(FilterValues.Compare(7.0, new FilterValue { Kind = FilterValueKind.UnsignedInteger, UnsignedInteger = 7UL }, FilterOperator.Equal));

    [Fact] public void AreNumberEqual_ExactDecimalActual_Match() =>
        Assert.True(FilterValues.Compare(42m, FilterValue.From(42.0), FilterOperator.Equal));

    [Fact] public void AreNumberEqual_ExactDecimalActual_NoMatch() =>
        Assert.False(FilterValues.Compare(42m, FilterValue.From(43.0), FilterOperator.Equal));

    [Fact] public void AreNumberEqual_DoubleActual_Match() =>
        Assert.True(FilterValues.Compare(3.14, FilterValue.From(3.14), FilterOperator.Equal));

    [Fact] public void AreDecimalEqual_ExactDecimal_Match() =>
        Assert.True(FilterValues.Compare(1.5m, new FilterValue { Kind = FilterValueKind.Decimal, Decimal = 1.5m }, FilterOperator.Equal));

    [Fact] public void AreDecimalEqual_DoubleActual_Match() =>
        Assert.True(FilterValues.Compare(2.0, new FilterValue { Kind = FilterValueKind.Decimal, Decimal = 2m }, FilterOperator.Equal));

    [Theory]
    [InlineData((byte)1)]
    [InlineData((sbyte)1)]
    [InlineData((short)1)]
    [InlineData((ushort)1)]
    public void TryCompareInteger_SmallTypes_GreaterThanZero(object value) =>
        Assert.True(FilterValues.Compare(value, new FilterValue { Kind = FilterValueKind.Integer, Integer = 0L }, FilterOperator.GreaterThan));

    [Fact]
    public void TryCompareInteger_ULong_BeyondLongMax_GreaterThanLongMax()
    {
        ulong big = (ulong)long.MaxValue + 5UL;
        Assert.True(FilterValues.Compare(big, new FilterValue { Kind = FilterValueKind.Integer, Integer = long.MaxValue }, FilterOperator.GreaterThan));
    }

    [Fact] public void TryCompareInteger_Decimal_Works() =>
        Assert.True(FilterValues.Compare(10m, new FilterValue { Kind = FilterValueKind.Integer, Integer = 10L }, FilterOperator.Equal));

    [Fact] public void TryCompareInteger_NonNumeric_ReturnsFalse() =>
        Assert.False(FilterValues.Compare("nope", new FilterValue { Kind = FilterValueKind.Integer, Integer = 1L }, FilterOperator.Equal));

    [Fact] public void TryCompareUnsignedInteger_NegativeSigned_LessThan() =>
        Assert.True(FilterValues.Compare(-1L, new FilterValue { Kind = FilterValueKind.UnsignedInteger, UnsignedInteger = 0UL }, FilterOperator.LessThan));

    [Fact] public void TryCompareUnsignedInteger_Decimal_Works() =>
        Assert.True(FilterValues.Compare(5m, new FilterValue { Kind = FilterValueKind.UnsignedInteger, UnsignedInteger = 5UL }, FilterOperator.Equal));

    [Fact] public void TryCompareExactNumber_ExactDecimalActual_Works() =>
        Assert.True(FilterValues.Compare(42L, FilterValue.From(42.0), FilterOperator.Equal));

    [Fact] public void TryCompareExactNumber_FloatActual_FallsThroughToTryNumber() =>
        Assert.True(FilterValues.Compare(1.5f, FilterValue.From(1.5), FilterOperator.Equal));

    [Fact] public void TryCompareDecimal_ExactActual_Works() =>
        Assert.True(FilterValues.Compare(2m, new FilterValue { Kind = FilterValueKind.Decimal, Decimal = 2m }, FilterOperator.Equal));

    [Fact] public void TryCompareDecimal_DoubleActual_Works() =>
        Assert.True(FilterValues.Compare(3.5, new FilterValue { Kind = FilterValueKind.Decimal, Decimal = 3.5m }, FilterOperator.LessThanOrEqual));

    [Fact] public void TryCompareDecimal_NonNumericActual_ReturnsFalse() =>
        Assert.False(FilterValues.Compare("abc", new FilterValue { Kind = FilterValueKind.Decimal, Decimal = 1m }, FilterOperator.Equal));

    [Theory]
    [InlineData((byte)1)]
    [InlineData((sbyte)2)]
    [InlineData((short)3)]
    [InlineData((ushort)4)]
    [InlineData(5)]
    [InlineData(6u)]
    [InlineData(7L)]
    [InlineData(8UL)]
    [InlineData(9.0f)]
    [InlineData(10.0)]
    public void TryNumber_AllBoxedNumericTypes_GreaterThanZero(object value) =>
        Assert.True(FilterValues.Compare(value, FilterValue.From(0L), FilterOperator.GreaterThan));

    [Fact] public void TryNumber_DecimalActual_GreaterThanZero() =>
        Assert.True(FilterValues.Compare(1.5m, FilterValue.From(0L), FilterOperator.GreaterThan));

    [Fact] public void TryNumber_StringActual_ReturnsFalse() =>
        Assert.False(FilterValues.Compare("abc", FilterValue.From(0L), FilterOperator.GreaterThan));

    [Fact] public void FilterValues_Compare_UnknownOperator_ReturnsFalse() =>
        Assert.False(FilterValues.Compare(1, FilterValue.From(1L), (FilterOperator)99));

    [Fact] public void FilterValues_Contains_OversizedCollection_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            FilterValues.Contains(new int[257], FilterValue.From(0L)));

    [Fact] public void FilterValues_Contains_Null_ReturnsFalse() =>
        Assert.False(FilterValues.Contains(null, FilterValue.From(1L)));

    [Fact]
    public void FilterValues_Contains_OversizedEnumerable_Throws()
    {
        static IEnumerable<int> LargeSeq() { for (int i = 0; i < 300; i++) yield return i; }
        Assert.Throws<InvalidOperationException>(() =>
            FilterValues.Contains(LargeSeq(), FilterValue.From(299L)));
    }

    [Fact] public void FilterValues_In_NullActual_WithNullValue_ReturnsTrue() =>
        Assert.True(FilterValues.In(null, [FilterValue.Null]));

    [Fact]
    public void FilterValues_In_GuidActual_Match()
    {
        var g = Guid.NewGuid();
        Assert.True(FilterValues.In(g, [FilterValue.From(g)]));
        Assert.False(FilterValues.In(Guid.NewGuid(), [FilterValue.From(g)]));
    }

    [Fact] public void FilterValues_In_BooleanActual_Match()
    {
        Assert.True(FilterValues.In(true, [FilterValue.From(true)]));
        Assert.False(FilterValues.In(false, [FilterValue.From(true)]));
    }
}

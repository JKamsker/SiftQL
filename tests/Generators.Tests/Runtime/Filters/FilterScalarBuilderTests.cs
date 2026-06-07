using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterScalarBuilderTests
{
    private sealed record ScalarSubject(
        int Count = 0,
        int? OptionalCount = null,
        long LongVal = 0,
        ulong ULongVal = 0,
        double DoubleVal = 0.0,
        decimal DecimalVal = 0m,
        float FloatVal = 0f,
        byte ByteVal = 0,
        sbyte SByteVal = 0,
        short ShortVal = 0,
        ushort UShortVal = 0,
        uint UIntVal = 0u,
        bool Active = false,
        string? Name = null,
        Guid Token = default,
        TestStatus Status = TestStatus.None) : IFilterSubject;

    public enum TestStatus { None = 0, Active = 1, Inactive = 2 }

    private static CompiledKernel Compile(FilterExpression filter) =>
        FilterCompiler.Compile(typeof(ScalarSubject), filter, FilterCompilerOptions.Immediate);

    [Fact] public void ScalarBuilder_NullValue_Equal_MatchesNull()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.Name), FilterOperator.Equal, FilterValue.Null));
        Assert.True(kernel.Matches(new ScalarSubject(Name: null)));
        Assert.False(kernel.Matches(new ScalarSubject(Name: "x")));
    }

    [Theory]
    [InlineData(FilterOperator.Equal, 10, 10, true)]
    [InlineData(FilterOperator.NotEqual, 10, 10, false)]
    [InlineData(FilterOperator.GreaterThan, 11, 10, true)]
    [InlineData(FilterOperator.GreaterThan, 10, 10, false)]
    [InlineData(FilterOperator.LessThan, 9, 10, true)]
    [InlineData(FilterOperator.LessThanOrEqual, 10, 10, true)]
    [InlineData(FilterOperator.GreaterThanOrEqual, 10, 10, true)]
    public void ScalarBuilder_Int32_AllOperators(FilterOperator op, int actual, int expected, bool shouldMatch)
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.Count), op, FilterValue.From((long)expected)));
        Assert.Equal(shouldMatch, kernel.Matches(new ScalarSubject(Count: actual)));
    }

    [Fact]
    public void ScalarBuilder_NullValue_NotEqual_MatchesNonNull()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.Name), FilterOperator.NotEqual, FilterValue.Null));
        Assert.False(kernel.Matches(new ScalarSubject(Name: null)));
        Assert.True(kernel.Matches(new ScalarSubject(Name: "x")));
    }

    [Fact]
    public void ScalarBuilder_EnumEqual_Matches()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.Status), FilterOperator.Equal, FilterValue.From(1L)));
        Assert.True(kernel.Matches(new ScalarSubject(Status: TestStatus.Active)));
        Assert.False(kernel.Matches(new ScalarSubject(Status: TestStatus.None)));
    }

    [Fact]
    public void ScalarBuilder_EnumNotEqual_Matches()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.Status), FilterOperator.NotEqual, FilterValue.From(1L)));
        Assert.False(kernel.Matches(new ScalarSubject(Status: TestStatus.Active)));
        Assert.True(kernel.Matches(new ScalarSubject(Status: TestStatus.None)));
    }

    [Fact]
    public void ScalarBuilder_BooleanEqual_Matches()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.Active), FilterOperator.Equal, FilterValue.From(true)));
        Assert.True(kernel.Matches(new ScalarSubject(Active: true)));
        Assert.False(kernel.Matches(new ScalarSubject(Active: false)));
    }

    [Fact]
    public void ScalarBuilder_BooleanNotEqual_Matches()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.Active), FilterOperator.NotEqual, FilterValue.From(true)));
        Assert.False(kernel.Matches(new ScalarSubject(Active: true)));
        Assert.True(kernel.Matches(new ScalarSubject(Active: false)));
    }

    [Fact]
    public void ScalarBuilder_GuidEqual_Matches()
    {
        var g = Guid.NewGuid();
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.Token), FilterOperator.Equal, FilterValue.From(g)));
        Assert.True(kernel.Matches(new ScalarSubject(Token: g)));
        Assert.False(kernel.Matches(new ScalarSubject(Token: Guid.NewGuid())));
    }

    [Fact]
    public void ScalarBuilder_Long_NegativeExpected_Equal_Matches()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.LongVal), FilterOperator.Equal, FilterValue.From(-100L)));
        Assert.True(kernel.Matches(new ScalarSubject(LongVal: -100L)));
        Assert.False(kernel.Matches(new ScalarSubject(LongVal: -99L)));
    }

    [Fact]
    public void ScalarBuilder_UInt32_NegativeExpected_NotEqual_IsTrue()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.UIntVal), FilterOperator.NotEqual, FilterValue.From(-1L)));
        Assert.True(kernel.Matches(new ScalarSubject(UIntVal: 0u)));
    }

    [Fact]
    public void ScalarBuilder_UInt32_NegativeExpected_GreaterThan_IsTrue()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.UIntVal), FilterOperator.GreaterThan, FilterValue.From(-1L)));
        Assert.True(kernel.Matches(new ScalarSubject(UIntVal: 0u)));
    }

    [Fact]
    public void ScalarBuilder_UInt32_NegativeExpected_GreaterThanOrEqual_IsTrue()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.UIntVal), FilterOperator.GreaterThanOrEqual, FilterValue.From(-1L)));
        Assert.True(kernel.Matches(new ScalarSubject(UIntVal: 0u)));
    }

    [Fact]
    public void ScalarBuilder_UInt32_NegativeExpected_LessThan_IsFalse()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.UIntVal), FilterOperator.LessThan, FilterValue.From(-1L)));
        Assert.False(kernel.Matches(new ScalarSubject(UIntVal: 0u)));
    }

    [Fact]
    public void ScalarBuilder_UInt32_NegativeExpected_Equal_IsFalse()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.UIntVal), FilterOperator.Equal, FilterValue.From(-1L)));
        Assert.False(kernel.Matches(new ScalarSubject(UIntVal: 0u)));
    }

    [Fact]
    public void ScalarBuilder_UInt64Value_SmallValue_SignedActual_Match()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.LongVal), FilterOperator.Equal, FilterValue.From(42UL)));
        Assert.True(kernel.Matches(new ScalarSubject(LongVal: 42L)));
        Assert.False(kernel.Matches(new ScalarSubject(LongVal: 43L)));
    }

    [Fact]
    public void ScalarBuilder_UInt64Value_BeyondLongMax_SignedActual_LessThan_IsTrue()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.LongVal), FilterOperator.LessThan, FilterValue.From(big)));
        Assert.True(kernel.Matches(new ScalarSubject(LongVal: 0L)));
    }

    [Fact]
    public void ScalarBuilder_UInt64Value_BeyondLongMax_SignedActual_LessThanOrEqual_IsTrue()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.LongVal), FilterOperator.LessThanOrEqual, FilterValue.From(big)));
        Assert.True(kernel.Matches(new ScalarSubject(LongVal: 0L)));
    }

    [Fact]
    public void ScalarBuilder_UInt64Value_BeyondLongMax_SignedActual_NotEqual_IsTrue()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.LongVal), FilterOperator.NotEqual, FilterValue.From(big)));
        Assert.True(kernel.Matches(new ScalarSubject(LongVal: 0L)));
    }

    [Fact]
    public void ScalarBuilder_UInt64Value_BeyondLongMax_SignedActual_GreaterThan_IsFalse()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.LongVal), FilterOperator.GreaterThan, FilterValue.From(big)));
        Assert.False(kernel.Matches(new ScalarSubject(LongVal: long.MaxValue)));
    }

    [Fact]
    public void ScalarBuilder_UInt64Value_BeyondLongMax_SignedActual_Equal_IsFalse()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.LongVal), FilterOperator.Equal, FilterValue.From(big)));
        Assert.False(kernel.Matches(new ScalarSubject(LongVal: long.MaxValue)));
    }

    [Fact]
    public void ScalarBuilder_UInt64Value_UnsignedActual_Match()
    {
        ulong val = 9_007_199_254_740_993UL;
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.ULongVal), FilterOperator.Equal, FilterValue.From(val)));
        Assert.True(kernel.Matches(new ScalarSubject(ULongVal: val)));
        Assert.False(kernel.Matches(new ScalarSubject(ULongVal: val - 1UL)));
    }

    [Fact]
    public void ScalarBuilder_Decimal_NumberKind_ExactRange_Match()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.DecimalVal), FilterOperator.Equal, FilterValue.From(1.5)));
        Assert.True(kernel.Matches(new ScalarSubject(DecimalVal: 1.5m)));
    }

    [Fact]
    public void ScalarBuilder_Decimal_NumberKind_OutOfExactRange_NotEqual_IsTrue()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.DecimalVal), FilterOperator.NotEqual, FilterValue.From(double.MaxValue)));
        Assert.True(kernel.Matches(new ScalarSubject(DecimalVal: 1m)));
    }

    [Fact]
    public void ScalarBuilder_Decimal_NumberKind_OutOfExactRange_Equal_IsFalse()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.DecimalVal), FilterOperator.Equal, FilterValue.From(double.MaxValue)));
        Assert.False(kernel.Matches(new ScalarSubject(DecimalVal: 1m)));
    }

    [Fact]
    public void ScalarBuilder_Decimal_NumberKind_NaN_IsFalse()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.DecimalVal), FilterOperator.GreaterThan, FilterValue.From(double.NaN)));
        Assert.False(kernel.Matches(new ScalarSubject(DecimalVal: 1m)));
    }

    [Fact]
    public void ScalarBuilder_Decimal_NumberKind_OutOfExactRange_GreaterThan_FallsBackToDouble()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.DecimalVal), FilterOperator.GreaterThan, FilterValue.From(double.MaxValue)));
        Assert.False(kernel.Matches(new ScalarSubject(DecimalVal: 1m)));
    }

    [Fact]
    public void ScalarBuilder_Decimal_DecimalKind_Match()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.DecimalVal), FilterOperator.Equal, FilterValue.From(1.5m)));
        Assert.True(kernel.Matches(new ScalarSubject(DecimalVal: 1.5m)));
        Assert.False(kernel.Matches(new ScalarSubject(DecimalVal: 2.5m)));
    }

    [Fact]
    public void ScalarBuilder_NullableInt_NotEqual_NullActual_IsTrue()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.OptionalCount), FilterOperator.NotEqual, FilterValue.From(5L)));
        Assert.True(kernel.Matches(new ScalarSubject(OptionalCount: null)));
        Assert.True(kernel.Matches(new ScalarSubject(OptionalCount: 6)));
        Assert.False(kernel.Matches(new ScalarSubject(OptionalCount: 5)));
    }

    [Fact]
    public void ScalarBuilder_NullableInt_Equal_NullActual_IsFalse()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.OptionalCount), FilterOperator.Equal, FilterValue.From(5L)));
        Assert.False(kernel.Matches(new ScalarSubject(OptionalCount: null)));
        Assert.True(kernel.Matches(new ScalarSubject(OptionalCount: 5)));
    }

    [Fact]
    public void ScalarBuilder_StringEqual_CaseSensitive()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.Name), FilterOperator.Equal, FilterValue.From("hello")));
        Assert.True(kernel.Matches(new ScalarSubject(Name: "hello")));
        Assert.False(kernel.Matches(new ScalarSubject(Name: "HELLO")));
    }

    [Fact]
    public void ScalarBuilder_StringNotEqual_Matches()
    {
        var kernel = Compile(FilterExpression.Compare(nameof(ScalarSubject.Name), FilterOperator.NotEqual, FilterValue.From("hello")));
        Assert.False(kernel.Matches(new ScalarSubject(Name: "hello")));
        Assert.True(kernel.Matches(new ScalarSubject(Name: "other")));
    }
}

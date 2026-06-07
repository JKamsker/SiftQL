using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class Wave3CoverageTests
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
        TestStatus Status = TestStatus.None) : IFilterSubject
    {
        public int[] Tags { get; init; } = [];
        public string?[] Labels { get; init; } = [];
        public Guid[] Tokens { get; init; } = [];
        public byte[] Bytes { get; init; } = [];
        public sbyte[] SBytes { get; init; } = [];
        public short[] Shorts { get; init; } = [];
        public ushort[] UShorts { get; init; } = [];
        public uint[] UInts { get; init; } = [];
        public long[] Longs { get; init; } = [];
        public ulong[] ULongs { get; init; } = [];
        public float[] Floats { get; init; } = [];
        public double[] Doubles { get; init; } = [];
        public decimal[] Decimals { get; init; } = [];
        public bool[] Flags { get; init; } = [];
    }

    public enum TestStatus { None = 0, Active = 1, Inactive = 2 }

    private static CompiledKernel Compile(FilterExpression filter) =>
        FilterCompiler.Compile(typeof(ScalarSubject), filter, FilterCompilerOptions.Immediate);

    // =========================================================================
    // FilterExpressionHelpers (internal - exercised via FilterCompiler)
    // =========================================================================

    [Fact]
    public void NumberIn_MatchesWhenPresent()
    {
        Assert.True(Compile(FilterExpression.In(nameof(ScalarSubject.DoubleVal),
            [FilterValue.From(1.0), FilterValue.From(2.0)]))
            .Matches(new ScalarSubject(DoubleVal: 1.0)));
    }

    [Fact]
    public void NumberIn_ReturnsFalseWhenAbsent()
    {
        Assert.False(Compile(FilterExpression.In(nameof(ScalarSubject.DoubleVal),
            [FilterValue.From(1.0), FilterValue.From(2.0)]))
            .Matches(new ScalarSubject(DoubleVal: 9.0)));
    }

    [Fact]
    public void NumberIn_LargeList_UsesHashSetBranch()
    {
        double[] vals = [1.0, 2.0, 3.0, 4.0, 5.0];
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.DoubleVal),
            vals.Select(FilterValue.From).ToArray()));
        Assert.True(kernel.Matches(new ScalarSubject(DoubleVal: 3.0)));
        Assert.False(kernel.Matches(new ScalarSubject(DoubleVal: 6.0)));
    }

    [Fact]
    public void NumberIn_Nullable_NullActual_WithNullInSet_ReturnsTrue()
    {
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.OptionalCount),
            [FilterValue.Null, FilterValue.From(1L)]));
        Assert.True(kernel.Matches(new ScalarSubject(OptionalCount: null)));
        Assert.True(kernel.Matches(new ScalarSubject(OptionalCount: 1)));
        Assert.False(kernel.Matches(new ScalarSubject(OptionalCount: 2)));
    }

    [Fact]
    public void NumberIn_LargeList_Nullable_HashSetBranch()
    {
        long[] vals = [1L, 2L, 3L, 4L, 5L];
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.OptionalCount),
            vals.Select(FilterValue.From).ToArray()));
        Assert.False(kernel.Matches(new ScalarSubject(OptionalCount: null)));
        Assert.True(kernel.Matches(new ScalarSubject(OptionalCount: 4)));
    }

    [Fact]
    public void NumberIn_IntegerKind_MatchesViaLong()
    {
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.DoubleVal),
            [FilterValue.From(42L), FilterValue.From(100L)]));
        Assert.True(kernel.Matches(new ScalarSubject(DoubleVal: 42.0)));
        Assert.False(kernel.Matches(new ScalarSubject(DoubleVal: 43.0)));
    }

    [Fact]
    public void NumberIn_DecimalKind_MatchesViaDecimalCast()
    {
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.DoubleVal),
            [FilterValue.From(1.5m), FilterValue.From(2.5m)]));
        Assert.True(kernel.Matches(new ScalarSubject(DoubleVal: 1.5)));
        Assert.False(kernel.Matches(new ScalarSubject(DoubleVal: 3.5)));
    }

    [Fact]
    public void NumberIn_NaNFilteredOut_NonNanStillMatches()
    {
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.DoubleVal),
            [FilterValue.From(double.NaN), FilterValue.From(1.0)]));
        Assert.True(kernel.Matches(new ScalarSubject(DoubleVal: 1.0)));
    }

    [Fact]
    public void StringIn_NullActual_ReturnsHasNullFlag()
    {
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.Name),
            [FilterValue.Null, FilterValue.From("a")]));
        Assert.True(kernel.Matches(new ScalarSubject(Name: null)));
        Assert.True(kernel.Matches(new ScalarSubject(Name: "a")));
        Assert.False(kernel.Matches(new ScalarSubject(Name: "b")));
    }

    [Fact]
    public void StringIn_NullActual_ReturnsFalse_WhenNoNullInSet()
    {
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.Name),
            [FilterValue.From("x"), FilterValue.From("y")]));
        Assert.False(kernel.Matches(new ScalarSubject(Name: null)));
    }

    [Fact]
    public void StringIn_LargeList_UsesHashSetBranch()
    {
        string[] vals = ["a", "b", "c", "d", "e"];
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.Name),
            vals.Select(FilterValue.From).ToArray()));
        Assert.True(kernel.Matches(new ScalarSubject(Name: "c")));
        Assert.False(kernel.Matches(new ScalarSubject(Name: "z")));
        Assert.False(kernel.Matches(new ScalarSubject(Name: null)));
    }

    [Fact]
    public void StringIn_LargeList_NullActual_WithNullInSet_ReturnsTrue()
    {
        var values = new FilterValue[] { FilterValue.From("a"), FilterValue.From("b"),
            FilterValue.From("c"), FilterValue.From("d"), FilterValue.Null };
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.Name), values));
        Assert.True(kernel.Matches(new ScalarSubject(Name: null)));
    }

    [Fact]
    public void GuidIn_SmallList_MatchesPresent()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.Token),
            [FilterValue.From(g1), FilterValue.From(g2)]));
        Assert.True(kernel.Matches(new ScalarSubject(Token: g1)));
        Assert.False(kernel.Matches(new ScalarSubject(Token: Guid.NewGuid())));
    }

    [Fact]
    public void GuidIn_LargeList_UsesHashSetBranch()
    {
        var guids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var target = guids[3];
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.Token),
            guids.Select(FilterValue.From).ToArray()));
        Assert.True(kernel.Matches(new ScalarSubject(Token: target)));
        Assert.False(kernel.Matches(new ScalarSubject(Token: Guid.NewGuid())));
    }

    [Fact]
    public void EnumIn_SmallList_IntegerValues_MatchesCorrectly()
    {
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.Status),
            [FilterValue.From(1L), FilterValue.From(2L)]));
        Assert.True(kernel.Matches(new ScalarSubject(Status: TestStatus.Active)));
        Assert.True(kernel.Matches(new ScalarSubject(Status: TestStatus.Inactive)));
        Assert.False(kernel.Matches(new ScalarSubject(Status: TestStatus.None)));
    }

    [Fact]
    public void EnumIn_LargeList_UsesHashSetBranch()
    {
        long[] vals = [0L, 1L, 2L, 3L, 4L];
        var kernel = Compile(FilterExpression.In(nameof(ScalarSubject.Count),
            vals.Select(FilterValue.From).ToArray()));
        Assert.True(kernel.Matches(new ScalarSubject(Count: 2)));
        Assert.False(kernel.Matches(new ScalarSubject(Count: 99)));
    }

    // =========================================================================
    // FilterArrayContains
    // =========================================================================

    [Fact] public void ContainsBoolean_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsBoolean(null, true));
    [Fact] public void ContainsBoolean_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsBoolean(new bool[257], true));
    [Fact] public void ContainsBoolean_TrueMatch() => Assert.True(FilterArrayContains.ContainsBoolean(new bool[] { true }, true));
    [Fact] public void ContainsBoolean_FalseMatch() => Assert.True(FilterArrayContains.ContainsBoolean(new bool[] { false }, false));
    [Fact] public void ContainsBoolean_NoMatch() => Assert.False(FilterArrayContains.ContainsBoolean(new bool[] { true }, false));

    [Fact] public void ContainsByte_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsByte(null, 1.0));
    [Fact] public void ContainsByte_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsByte(new byte[257], 1.0));
    [Fact] public void ContainsByte_Match() => Assert.True(FilterArrayContains.ContainsByte(new byte[] { 1, 2, 3 }, 2.0));
    [Fact] public void ContainsByte_NoMatch() => Assert.False(FilterArrayContains.ContainsByte(new byte[] { 1, 2, 3 }, 9.0));
    [Fact] public void ContainsByteValue_Match() => Assert.True(FilterArrayContains.ContainsByteValue(new byte[] { 10, 20 }, 20));
    [Fact] public void ContainsByteValue_NoMatch() => Assert.False(FilterArrayContains.ContainsByteValue(new byte[] { 10, 20 }, 30));
    [Fact] public void ContainsByteValue_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsByteValue(null, 1));

    [Fact] public void ContainsSByte_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsSByte(null, 1.0));
    [Fact] public void ContainsSByte_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsSByte(new sbyte[257], 1.0));
    [Fact] public void ContainsSByte_Match() => Assert.True(FilterArrayContains.ContainsSByte(new sbyte[] { 2, 3 }, 2.0));
    [Fact] public void ContainsSByte_NoMatch() => Assert.False(FilterArrayContains.ContainsSByte(new sbyte[] { 2, 3 }, 99.0));
    [Fact] public void ContainsSByteValue_Match() => Assert.True(FilterArrayContains.ContainsSByteValue(new sbyte[] { 10, 20 }, 20));
    [Fact] public void ContainsSByteValue_NoMatch() => Assert.False(FilterArrayContains.ContainsSByteValue(new sbyte[] { 10, 20 }, 5));
    [Fact] public void ContainsSByteValue_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsSByteValue(null, 1));

    [Fact] public void ContainsInt16_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt16(null, 1.0));
    [Fact] public void ContainsInt16_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt16(new short[257], 1.0));
    [Fact] public void ContainsInt16_Match() => Assert.True(FilterArrayContains.ContainsInt16(new short[] { 100, 200 }, 100.0));
    [Fact] public void ContainsInt16_NoMatch() => Assert.False(FilterArrayContains.ContainsInt16(new short[] { 100, 200 }, 300.0));
    [Fact] public void ContainsInt16Value_Match() => Assert.True(FilterArrayContains.ContainsInt16Value(new short[] { 1, 2 }, 2));
    [Fact] public void ContainsInt16Value_NoMatch() => Assert.False(FilterArrayContains.ContainsInt16Value(new short[] { 1, 2 }, 3));
    [Fact] public void ContainsInt16Value_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt16Value(null, 1));

    [Fact] public void ContainsUInt16_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt16(null, 1.0));
    [Fact] public void ContainsUInt16_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt16(new ushort[257], 1.0));
    [Fact] public void ContainsUInt16_Match() => Assert.True(FilterArrayContains.ContainsUInt16(new ushort[] { 500, 600 }, 600.0));
    [Fact] public void ContainsUInt16_NoMatch() => Assert.False(FilterArrayContains.ContainsUInt16(new ushort[] { 500, 600 }, 700.0));
    [Fact] public void ContainsUInt16Value_Match() => Assert.True(FilterArrayContains.ContainsUInt16Value(new ushort[] { 10, 20 }, 10));
    [Fact] public void ContainsUInt16Value_NoMatch() => Assert.False(FilterArrayContains.ContainsUInt16Value(new ushort[] { 10, 20 }, 30));
    [Fact] public void ContainsUInt16Value_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt16Value(null, 1));

    [Fact] public void ContainsInt32_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt32(null, 1.0));
    [Fact] public void ContainsInt32_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt32(new int[257], 1.0));
    [Fact] public void ContainsInt32_FractionalDouble_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt32(new int[] { 1, 2, 3 }, 1.5));
    [Fact] public void ContainsInt32_DoubleOutOfRange_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt32(new int[] { 1 }, (double)int.MaxValue + 1.0));
    [Fact] public void ContainsInt32_Match() => Assert.True(FilterArrayContains.ContainsInt32(new int[] { 42, 99 }, 42.0));
    [Fact] public void ContainsInt32_NoMatch() => Assert.False(FilterArrayContains.ContainsInt32(new int[] { 42, 99 }, 100.0));
    [Fact] public void ContainsInt32Value_Match() => Assert.True(FilterArrayContains.ContainsInt32Value(new int[] { 1, 2, 3 }, 3));
    [Fact] public void ContainsInt32Value_NoMatch() => Assert.False(FilterArrayContains.ContainsInt32Value(new int[] { 1, 2, 3 }, 4));
    [Fact] public void ContainsInt32Value_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt32Value(null, 1));

    [Fact] public void ContainsUInt32_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt32(null, 1.0));
    [Fact] public void ContainsUInt32_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt32(new uint[257], 1.0));
    [Fact] public void ContainsUInt32_FractionalDouble_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt32(new uint[] { 1u, 2u }, 1.5));
    [Fact] public void ContainsUInt32_NegativeDouble_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt32(new uint[] { 1u }, -1.0));
    [Fact] public void ContainsUInt32_Match() => Assert.True(FilterArrayContains.ContainsUInt32(new uint[] { 100u, 200u }, 100.0));
    [Fact] public void ContainsUInt32_NoMatch() => Assert.False(FilterArrayContains.ContainsUInt32(new uint[] { 100u, 200u }, 300.0));
    [Fact] public void ContainsUInt32Value_Match() => Assert.True(FilterArrayContains.ContainsUInt32Value(new uint[] { 1u, 2u }, 1u));
    [Fact] public void ContainsUInt32Value_NoMatch() => Assert.False(FilterArrayContains.ContainsUInt32Value(new uint[] { 1u, 2u }, 3u));
    [Fact] public void ContainsUInt32Value_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt32Value(null, 1u));

    [Fact] public void ContainsInt64_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt64(null, 1.0));
    [Fact] public void ContainsInt64_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt64(new long[257], 1.0));
    [Fact] public void ContainsInt64_FractionalDouble_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt64(new long[] { 1L }, 1.5));
    [Fact] public void ContainsInt64_Match() => Assert.True(FilterArrayContains.ContainsInt64(new long[] { 100L, 200L }, 100.0));
    [Fact] public void ContainsInt64_NoMatch() => Assert.False(FilterArrayContains.ContainsInt64(new long[] { 100L, 200L }, 300.0));
    [Fact] public void ContainsInt64Value_Match() => Assert.True(FilterArrayContains.ContainsInt64Value(new long[] { 10L, 20L }, 20L));
    [Fact] public void ContainsInt64Value_NoMatch() => Assert.False(FilterArrayContains.ContainsInt64Value(new long[] { 10L, 20L }, 30L));
    [Fact] public void ContainsInt64Value_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsInt64Value(null, 1L));

    [Fact] public void ContainsUInt64_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt64(null, 1.0));
    [Fact] public void ContainsUInt64_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt64(new ulong[257], 1.0));
    [Fact] public void ContainsUInt64_FractionalDouble_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt64(new ulong[] { 1UL }, 1.5));
    [Fact] public void ContainsUInt64_NegativeDouble_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt64(new ulong[] { 1UL }, -1.0));
    [Fact] public void ContainsUInt64_Match() => Assert.True(FilterArrayContains.ContainsUInt64(new ulong[] { 42UL }, 42.0));
    [Fact] public void ContainsUInt64_NoMatch() => Assert.False(FilterArrayContains.ContainsUInt64(new ulong[] { 42UL }, 43.0));
    [Fact] public void ContainsUInt64Value_Match() => Assert.True(FilterArrayContains.ContainsUInt64Value(new ulong[] { 5UL, 6UL }, 6UL));
    [Fact] public void ContainsUInt64Value_NoMatch() => Assert.False(FilterArrayContains.ContainsUInt64Value(new ulong[] { 5UL, 6UL }, 7UL));
    [Fact] public void ContainsUInt64Value_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsUInt64Value(null, 1UL));

    [Fact] public void ContainsSingle_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsSingle(null, 1.0));
    [Fact] public void ContainsSingle_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsSingle(new float[257], 1.0f));
    [Fact] public void ContainsSingle_Match() => Assert.True(FilterArrayContains.ContainsSingle(new float[] { 1.5f, 2.5f }, 1.5));
    [Fact] public void ContainsSingle_NoMatch() => Assert.False(FilterArrayContains.ContainsSingle(new float[] { 1.5f }, 3.0));

    [Fact] public void ContainsDouble_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsDouble(null, 1.0));
    [Fact] public void ContainsDouble_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsDouble(new double[257], 1.0));
    [Fact] public void ContainsDouble_Match() => Assert.True(FilterArrayContains.ContainsDouble(new double[] { 3.14, 2.72 }, 3.14));
    [Fact] public void ContainsDouble_NoMatch() => Assert.False(FilterArrayContains.ContainsDouble(new double[] { 3.14, 2.72 }, 1.0));

    [Fact]
    public void ContainsDecimal_Overflow_ReturnsFalse() =>
        Assert.False(FilterArrayContains.ContainsDecimal(new decimal[] { 1.0m }, double.MaxValue));

    [Fact]
    public void ContainsDecimal_NoMatchInArray_ReturnsFalse() =>
        Assert.False(FilterArrayContains.ContainsDecimal(new decimal[] { 1.0m, 2.0m }, 3.0));

    [Fact] public void ContainsDecimalValue_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsDecimalValue(null, 1m));
    [Fact] public void ContainsDecimalValue_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsDecimalValue(new decimal[257], 1m));
    [Fact] public void ContainsDecimalValue_Match() => Assert.True(FilterArrayContains.ContainsDecimalValue(new decimal[] { 0.1m, 0.2m }, 0.2m));
    [Fact] public void ContainsDecimalValue_NoMatch() => Assert.False(FilterArrayContains.ContainsDecimalValue(new decimal[] { 0.1m, 0.2m }, 0.3m));

    [Fact] public void ContainsString_NullArray_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsString(null, "x"));
    [Fact] public void ContainsString_OversizedArray_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsString(new string[257], "x"));
    [Fact] public void ContainsString_NullExpected_MatchesNullElement() => Assert.True(FilterArrayContains.ContainsString(new string?[] { null, "a" }, null));
    [Fact] public void ContainsString_NullExpected_NoNullInArray_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsString(new string[] { "a", "b" }, null));
    [Fact] public void ContainsString_CaseSensitive_NoMatch() => Assert.False(FilterArrayContains.ContainsString(new string[] { "hello", "world" }, "WORLD"));
    [Fact] public void ContainsString_Match() => Assert.True(FilterArrayContains.ContainsString(new string[] { "hello", "world" }, "world"));

    [Fact] public void ContainsGuid_Null_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsGuid(null, Guid.Empty));
    [Fact] public void ContainsGuid_Oversized_ReturnsFalse() => Assert.False(FilterArrayContains.ContainsGuid(new Guid[257], Guid.Empty));

    [Fact]
    public void ContainsGuid_Match()
    {
        var g = Guid.NewGuid();
        Assert.True(FilterArrayContains.ContainsGuid(new Guid[] { Guid.Empty, g }, g));
    }

    [Fact]
    public void ContainsGuid_NoMatch()
    {
        var g = Guid.NewGuid();
        Assert.False(FilterArrayContains.ContainsGuid(new Guid[] { Guid.Empty, g }, Guid.NewGuid()));
    }

    // FilterNumericComparison
    [Fact] public void Num_Byte() => Assert.True(FilterValues.Compare((byte)1, FilterValue.From(1L), FilterOperator.Equal));
    // ===================================================================================
    // FilterNumericComparison (via FilterValues.Compare)
    // ===================================================================================


    // =========================================================================
    // FilterNumericComparison (via FilterValues.Compare)
    // =========================================================================

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

    // =========================================================================
    // FilterExpressionScalarBuilder (via FilterCompiler)
    // =========================================================================

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

    // =========================================================================
    // FilterIndexValue - TryCreate / TryCreateActual
    // =========================================================================

    [Fact] public void FilterIndexValue_TryCreate_Boolean_Succeeds()
    {
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(true), out var key));
        Assert.Equal(FilterValueKind.Boolean, key.Kind);
        Assert.True(key.Boolean);
    }

    [Fact] public void FilterIndexValue_TryCreate_Integer_Succeeds()
    {
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(42L), out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
        Assert.Equal(42L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreate_Number_Succeeds()
    {
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(3.14), out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
        Assert.Equal(3.14, key.Number);
    }

    [Fact] public void FilterIndexValue_TryCreate_String_Succeeds()
    {
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From("hello"), out var key));
        Assert.Equal(FilterValueKind.String, key.Kind);
        Assert.Equal("hello", key.String);
    }

    [Fact] public void FilterIndexValue_TryCreate_Null_ReturnsFalse() =>
        Assert.False(FilterIndexValue.TryCreate(FilterValue.Null, out _));

    [Fact] public void FilterIndexValue_TryCreate_Decimal_ReturnsFalse() =>
        Assert.False(FilterIndexValue.TryCreate(new FilterValue { Kind = FilterValueKind.Decimal, Decimal = 1.5m }, out _));

    [Fact]
    public void FilterIndexValue_TryCreate_UnsignedInteger_Succeeds()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(big), out var key));
        Assert.Equal(FilterValueKind.UnsignedInteger, key.Kind);
        Assert.Equal(big, key.UnsignedInteger);
    }

    [Fact]
    public void FilterIndexValue_TryCreate_Guid_Succeeds()
    {
        var g = Guid.NewGuid();
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(g), out var key));
        Assert.Equal(FilterValueKind.Guid, key.Kind);
        Assert.Equal(g, key.Guid);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Bool_Succeeds()
    {
        Assert.True(FilterIndexValue.TryCreateActual(true, out var key));
        Assert.Equal(FilterValueKind.Boolean, key.Kind);
        Assert.True(key.Boolean);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Byte_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual((byte)10, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
        Assert.Equal(10L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_SByte_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual((sbyte)-3, out var key));
        Assert.Equal(-3L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Short_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual((short)1000, out var key));
        Assert.Equal(1000L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_UShort_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual((ushort)2000, out var key));
        Assert.Equal(2000L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Int_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual(42, out var key));
        Assert.Equal(42L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_UInt_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual(99u, out var key));
        Assert.Equal(99L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Long_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual(123L, out var key));
        Assert.Equal(123L, key.Integer);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Float_ProducesNumber()
    {
        Assert.True(FilterIndexValue.TryCreateActual(1.5f, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Double_ProducesNumber()
    {
        Assert.True(FilterIndexValue.TryCreateActual(3.14, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Decimal_ProducesNumber()
    {
        Assert.True(FilterIndexValue.TryCreateActual(1.5m, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_String_ProducesString()
    {
        Assert.True(FilterIndexValue.TryCreateActual("test", out var key));
        Assert.Equal(FilterValueKind.String, key.Kind);
        Assert.Equal("test", key.String);
    }

    [Fact] public void FilterIndexValue_TryCreateActual_Null_ReturnsFalse() =>
        Assert.False(FilterIndexValue.TryCreateActual(null, out _));

    [Fact] public void FilterIndexValue_TryCreateActual_UnsupportedType_ReturnsFalse() =>
        Assert.False(FilterIndexValue.TryCreateActual(new object(), out _));

    [Fact] public void FilterIndexValue_TryCreateActual_Enum_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual(TestStatus.Active, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
        Assert.Equal(1L, key.Integer);
    }

    [Fact]
    public void FilterIndexValue_TryCreateActual_ULong_WithinLongMax_ProducesInteger()
    {
        Assert.True(FilterIndexValue.TryCreateActual(500UL, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
        Assert.Equal(500L, key.Integer);
    }

    [Fact]
    public void FilterIndexValue_TryCreateActual_ULong_BeyondLongMax_ProducesUnsignedInteger()
    {
        ulong big = (ulong)long.MaxValue + 10UL;
        Assert.True(FilterIndexValue.TryCreateActual(big, out var key));
        Assert.Equal(FilterValueKind.UnsignedInteger, key.Kind);
        Assert.Equal(big, key.UnsignedInteger);
    }

    [Fact]
    public void FilterIndexValue_TryCreateActual_Guid_ProducesGuid()
    {
        var g = Guid.NewGuid();
        Assert.True(FilterIndexValue.TryCreateActual(g, out var key));
        Assert.Equal(FilterValueKind.Guid, key.Kind);
        Assert.Equal(g, key.Guid);
    }

    // =========================================================================
    // ProjectionValueFactory
    // =========================================================================

    [Fact] public void ProjectionValueFactory_FromBoolean_True()
    {
        var v = ProjectionValueFactory.FromBoolean(true);
        Assert.Equal(ProjectedEventValueKind.Boolean, v.Kind);
        Assert.True(v.Boolean);
    }

    [Fact] public void ProjectionValueFactory_FromBoolean_NullableTrue() =>
        Assert.Equal(ProjectedEventValueKind.Boolean, ProjectionValueFactory.FromBoolean((bool?)true).Kind);

    [Fact] public void ProjectionValueFactory_FromBoolean_NullableNull() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromBoolean((bool?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromByte() =>
        Assert.Equal(5L, ProjectionValueFactory.FromByte((byte)5).Integer);

    [Fact] public void ProjectionValueFactory_FromByte_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromByte((byte?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromByte_Nullable_Value() =>
        Assert.Equal(10L, ProjectionValueFactory.FromByte((byte?)10).Integer);

    [Fact] public void ProjectionValueFactory_FromSByte() =>
        Assert.Equal(-3L, ProjectionValueFactory.FromSByte((sbyte)-3).Integer);

    [Fact] public void ProjectionValueFactory_FromSByte_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromSByte((sbyte?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromSByte_Nullable_Value() =>
        Assert.Equal(-5L, ProjectionValueFactory.FromSByte((sbyte?)-5).Integer);

    [Fact] public void ProjectionValueFactory_FromInt16() =>
        Assert.Equal(1000L, ProjectionValueFactory.FromInt16((short)1000).Integer);

    [Fact] public void ProjectionValueFactory_FromInt16_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromInt16((short?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromInt16_Nullable_Value() =>
        Assert.Equal(500L, ProjectionValueFactory.FromInt16((short?)500).Integer);

    [Fact] public void ProjectionValueFactory_FromUInt16() =>
        Assert.Equal(2000L, ProjectionValueFactory.FromUInt16((ushort)2000).Integer);

    [Fact] public void ProjectionValueFactory_FromUInt16_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromUInt16((ushort?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromUInt16_Nullable_Value() =>
        Assert.Equal(3000L, ProjectionValueFactory.FromUInt16((ushort?)3000).Integer);

    [Fact] public void ProjectionValueFactory_FromInt32() =>
        Assert.Equal(42L, ProjectionValueFactory.FromInt32(42).Integer);

    [Fact] public void ProjectionValueFactory_FromInt32_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromInt32((int?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromInt32_Nullable_Value() =>
        Assert.Equal(77L, ProjectionValueFactory.FromInt32((int?)77).Integer);

    [Fact] public void ProjectionValueFactory_FromUInt32() =>
        Assert.Equal(99L, ProjectionValueFactory.FromUInt32(99u).Integer);

    [Fact] public void ProjectionValueFactory_FromUInt32_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromUInt32((uint?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromUInt32_Nullable_Value() =>
        Assert.Equal(55L, ProjectionValueFactory.FromUInt32((uint?)55u).Integer);

    [Fact] public void ProjectionValueFactory_FromInt64() =>
        Assert.Equal(123L, ProjectionValueFactory.FromInt64(123L).Integer);

    [Fact] public void ProjectionValueFactory_FromInt64_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromInt64((long?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromInt64_Nullable_Value() =>
        Assert.Equal(999L, ProjectionValueFactory.FromInt64((long?)999L).Integer);

    [Fact]
    public void ProjectionValueFactory_FromUInt64_WithinLongRange()
    {
        var v = ProjectionValueFactory.FromUInt64(500UL);
        Assert.Equal(ProjectedEventValueKind.Integer, v.Kind);
        Assert.Equal(500L, v.Integer);
    }

    [Fact]
    public void ProjectionValueFactory_FromUInt64_BeyondLongMax()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        var v = ProjectionValueFactory.FromUInt64(big);
        Assert.Equal(ProjectedEventValueKind.UnsignedInteger, v.Kind);
        Assert.Equal(big, v.UnsignedInteger);
    }

    [Fact] public void ProjectionValueFactory_FromUInt64_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromUInt64((ulong?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromUInt64_Nullable_Value() =>
        Assert.Equal(ProjectedEventValueKind.Integer, ProjectionValueFactory.FromUInt64((ulong?)10UL).Kind);

    [Fact] public void ProjectionValueFactory_FromSingle() =>
        Assert.Equal(ProjectedEventValueKind.Number, ProjectionValueFactory.FromSingle(1.5f).Kind);

    [Fact] public void ProjectionValueFactory_FromSingle_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromSingle((float?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromSingle_Nullable_Value() =>
        Assert.Equal(ProjectedEventValueKind.Number, ProjectionValueFactory.FromSingle((float?)2.5f).Kind);

    [Fact] public void ProjectionValueFactory_FromDouble()
    {
        var v = ProjectionValueFactory.FromDouble(3.14);
        Assert.Equal(ProjectedEventValueKind.Number, v.Kind);
        Assert.Equal(3.14, v.Number);
    }

    [Fact] public void ProjectionValueFactory_FromDouble_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromDouble((double?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromDouble_Nullable_Value() =>
        Assert.Equal(ProjectedEventValueKind.Number, ProjectionValueFactory.FromDouble((double?)1.0).Kind);

    [Fact]
    public void ProjectionValueFactory_FromDecimal_Integral()
    {
        var v = ProjectionValueFactory.FromDecimal(42m);
        Assert.Equal(ProjectedEventValueKind.Integer, v.Kind);
        Assert.Equal(42L, v.Integer);
    }

    [Fact]
    public void ProjectionValueFactory_FromDecimal_Fractional() =>
        Assert.Equal(ProjectedEventValueKind.Decimal, ProjectionValueFactory.FromDecimal(1.5m).Kind);

    [Fact] public void ProjectionValueFactory_FromDecimal_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromDecimal((decimal?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromDecimal_Nullable_Value() =>
        Assert.Equal(ProjectedEventValueKind.Decimal, ProjectionValueFactory.FromDecimal((decimal?)1.5m).Kind);

    [Fact] public void ProjectionValueFactory_FromString_NonNull()
    {
        var v = ProjectionValueFactory.FromString("hello");
        Assert.Equal(ProjectedEventValueKind.String, v.Kind);
        Assert.Equal("hello", v.String);
    }

    [Fact] public void ProjectionValueFactory_FromString_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromString(null).Kind);

    [Fact]
    public void ProjectionValueFactory_FromGuid()
    {
        var g = Guid.NewGuid();
        var v = ProjectionValueFactory.FromGuid(g);
        Assert.Equal(ProjectedEventValueKind.Guid, v.Kind);
        Assert.Equal(g, v.Guid);
    }

    [Fact] public void ProjectionValueFactory_FromGuid_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromGuid((Guid?)null).Kind);

    [Fact]
    public void ProjectionValueFactory_FromGuid_Nullable_Value()
    {
        var g = Guid.NewGuid();
        var v = ProjectionValueFactory.FromGuid((Guid?)g);
        Assert.Equal(ProjectedEventValueKind.Guid, v.Kind);
        Assert.Equal(g, v.Guid);
    }

    [Fact] public void ProjectionValueFactory_FromEnum_ProducesString()
    {
        var v = ProjectionValueFactory.FromEnum(TestStatus.Active);
        Assert.Equal(ProjectedEventValueKind.String, v.Kind);
        Assert.Equal("Active", v.String);
    }

    [Fact] public void ProjectionValueFactory_FromEnum_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromEnum((TestStatus?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromEnum_Nullable_Value() =>
        Assert.Equal("Inactive", ProjectionValueFactory.FromEnum((TestStatus?)TestStatus.Inactive).String);

    [Fact] public void ProjectionValueFactory_FromObject_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromObject(null).Kind);

    [Fact] public void ProjectionValueFactory_FromObject_String() =>
        Assert.Equal(ProjectedEventValueKind.Object, ProjectionValueFactory.FromObject("hi").Kind);

    [Fact] public void ProjectionValueFactory_FromObject_Int() =>
        Assert.Equal(ProjectedEventValueKind.Object, ProjectionValueFactory.FromObject(42).Kind);

    [Fact] public void ProjectionValueFactory_FromObject_Bool() =>
        Assert.Equal(ProjectedEventValueKind.Object, ProjectionValueFactory.FromObject(true).Kind);

    // =========================================================================
    // CompiledKernelMatcher
    // =========================================================================

    [Fact]
    public void KernelMatcher_AlwaysTrue_ReturnsTrue()
    {
        var matcher = CompiledKernel.Any.CreateMatcher<ScalarSubject>();
        Assert.True(matcher.Matches(new ScalarSubject()));
    }

    [Fact]
    public void KernelMatcher_ImmediateKernel_MatchesAndRejects()
    {
        var filter = FilterExpression.Compare(nameof(ScalarSubject.Count), FilterOperator.Equal, FilterValue.From(7L));
        var kernel = FilterCompiler.Compile(typeof(ScalarSubject), filter, FilterCompilerOptions.Immediate);
        var matcher = kernel.CreateMatcher<ScalarSubject>();
        Assert.True(matcher.Matches(new ScalarSubject(Count: 7)));
        Assert.False(matcher.Matches(new ScalarSubject(Count: 8)));
    }

    [Fact]
    public void KernelMatcher_MultipleCallsNonTiered_StableResults()
    {
        var filter = FilterExpression.Compare(nameof(ScalarSubject.Name), FilterOperator.Equal, FilterValue.From("ok"));
        var kernel = FilterCompiler.Compile(typeof(ScalarSubject), filter, FilterCompilerOptions.Immediate);
        var matcher = kernel.CreateMatcher<ScalarSubject>();
        for (int i = 0; i < 10; i++)
        {
            Assert.True(matcher.Matches(new ScalarSubject(Name: "ok")));
            Assert.False(matcher.Matches(new ScalarSubject(Name: "no")));
        }
    }

    [Fact]
    public void KernelMatcher_ObjectPredicateOnly_FallsBack()
    {
        var kernel = new CompiledKernel(static obj => obj is ScalarSubject s && s.Count == 99, isBroad: false);
        var matcher = kernel.CreateMatcher<ScalarSubject>();
        Assert.True(matcher.Matches(new ScalarSubject(Count: 99)));
        Assert.False(matcher.Matches(new ScalarSubject(Count: 1)));
    }

    // =========================================================================
    // ParameterizedFilterPlan Nodes
    // =========================================================================

    [Fact]
    public void ConstantFilterPlanNode_True_AlwaysMatches()
    {
        var kernel = FilterCompiler.Compile(typeof(ScalarSubject), FilterExpression.Any, FilterCompilerOptions.Immediate);
        Assert.True(kernel.IsAlwaysTrue);
        Assert.True(kernel.Matches(new ScalarSubject()));
    }

    [Fact]
    public void NotFilterPlanNode_InvertsResult()
    {
        var inner = new FilterExpression
        {
            Kind = FilterExpressionKind.Compare,
            Field = nameof(ScalarSubject.Active),
            Operator = FilterOperator.Equal,
            Value = FilterValue.From(true) with { ParameterKey = "p0" },
        };
        var kernel = FilterCompiler.Compile(typeof(ScalarSubject), FilterExpression.Not(inner), FilterCompilerOptions.Immediate);
        Assert.False(kernel.Matches(new ScalarSubject(Active: true)));
        Assert.True(kernel.Matches(new ScalarSubject(Active: false)));
    }

    [Fact]
    public void NotFilterPlanNode_DoubleNegation_RestoresSemantics()
    {
        var inner = new FilterExpression
        {
            Kind = FilterExpressionKind.Compare,
            Field = nameof(ScalarSubject.Count),
            Operator = FilterOperator.Equal,
            Value = FilterValue.From(5L) with { ParameterKey = "p0" },
        };
        var kernel = FilterCompiler.Compile(typeof(ScalarSubject), FilterExpression.Not(FilterExpression.Not(inner)), FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ScalarSubject(Count: 5)));
        Assert.False(kernel.Matches(new ScalarSubject(Count: 6)));
    }

    [Fact]
    public void ExistsFilterPlanNode_NonNullField_ReturnsTrue()
    {
        var kernel = Compile(FilterExpression.Exists(nameof(ScalarSubject.Name)));
        Assert.True(kernel.Matches(new ScalarSubject(Name: "present")));
        Assert.False(kernel.Matches(new ScalarSubject(Name: null)));
    }

    [Fact]
    public void ExistsFilterPlanNode_CombinedWithParameterized_ForcesParameterizedPath()
    {
        var filter = FilterExpression.And(
            FilterExpression.Exists(nameof(ScalarSubject.Name)),
            new FilterExpression
            {
                Kind = FilterExpressionKind.Compare,
                Field = nameof(ScalarSubject.Count),
                Operator = FilterOperator.Equal,
                Value = FilterValue.From(1L) with { ParameterKey = "p0" },
            });
        var kernel = FilterCompiler.Compile(typeof(ScalarSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ScalarSubject(Name: "ok", Count: 1)));
        Assert.False(kernel.Matches(new ScalarSubject(Name: null, Count: 1)));
        Assert.False(kernel.Matches(new ScalarSubject(Name: "ok", Count: 2)));
    }

    [Fact]
    public void ContainsFilterPlanNode_IntArray_Match()
    {
        var kernel = Compile(FilterExpression.Contains(nameof(ScalarSubject.Tags), FilterValue.From(42L)));
        Assert.True(kernel.Matches(new ScalarSubject { Tags = [1, 42, 100] }));
        Assert.False(kernel.Matches(new ScalarSubject { Tags = [1, 2, 3] }));
    }

    [Fact]
    public void ContainsFilterPlanNode_StringArray_Match()
    {
        var kernel = Compile(FilterExpression.Contains(nameof(ScalarSubject.Labels), FilterValue.From("target")));
        Assert.True(kernel.Matches(new ScalarSubject { Labels = ["a", "target", "b"] }));
        Assert.False(kernel.Matches(new ScalarSubject { Labels = ["x", "y"] }));
    }

    [Fact]
    public void ContainsFilterPlanNode_GuidArray_Match()
    {
        var g = Guid.NewGuid();
        var kernel = Compile(FilterExpression.Contains(nameof(ScalarSubject.Tokens), FilterValue.From(g)));
        Assert.True(kernel.Matches(new ScalarSubject { Tokens = [Guid.NewGuid(), g] }));
        Assert.False(kernel.Matches(new ScalarSubject { Tokens = [Guid.NewGuid()] }));
    }

    [Fact]
    public void ContainsFilterPlanNode_WithParameterKey_BindsCorrectly()
    {
        var filter = FilterExpression.Contains(
            nameof(ScalarSubject.Tags),
            FilterValue.From(7L) with { ParameterKey = "p0" });
        var kernel = FilterCompiler.Compile(typeof(ScalarSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ScalarSubject { Tags = [5, 7, 9] }));
        Assert.False(kernel.Matches(new ScalarSubject { Tags = [1, 2, 3] }));
    }

    [Fact]
    public void ContainsFilterPlanNode_EmptyArray_ReturnsFalse()
    {
        var kernel = Compile(FilterExpression.Contains(nameof(ScalarSubject.Tags), FilterValue.From(1L)));
        Assert.False(kernel.Matches(new ScalarSubject { Tags = [] }));
    }

    [Fact]
    public void ContainsFilterPlanNode_BoolArray_Match()
    {
        var kernel = Compile(FilterExpression.Contains(nameof(ScalarSubject.Flags), FilterValue.From(true)));
        Assert.True(kernel.Matches(new ScalarSubject { Flags = [false, true] }));
        Assert.False(kernel.Matches(new ScalarSubject { Flags = [false] }));
    }

    [Fact]
    public void ContainsFilterPlanNode_ByteArray_Match()
    {
        var kernel = Compile(FilterExpression.Contains(nameof(ScalarSubject.Bytes), FilterValue.From(10L)));
        Assert.True(kernel.Matches(new ScalarSubject { Bytes = [5, 10, 15] }));
        Assert.False(kernel.Matches(new ScalarSubject { Bytes = [1, 2, 3] }));
    }

    [Fact]
    public void ContainsFilterPlanNode_LongArray_Match()
    {
        var kernel = Compile(FilterExpression.Contains(nameof(ScalarSubject.Longs), FilterValue.From(100L)));
        Assert.True(kernel.Matches(new ScalarSubject { Longs = [50L, 100L, 150L] }));
        Assert.False(kernel.Matches(new ScalarSubject { Longs = [1L, 2L] }));
    }

    [Fact]
    public void ContainsFilterPlanNode_DoubleArray_Match()
    {
        var kernel = Compile(FilterExpression.Contains(nameof(ScalarSubject.Doubles), FilterValue.From(3.14)));
        Assert.True(kernel.Matches(new ScalarSubject { Doubles = [1.0, 3.14] }));
        Assert.False(kernel.Matches(new ScalarSubject { Doubles = [1.0, 2.0] }));
    }

    // =========================================================================
    // FilterValues edge cases
    // =========================================================================

    [Fact] public void FilterValues_Compare_UnknownOperator_ReturnsFalse() =>
        Assert.False(FilterValues.Compare(1, FilterValue.From(1L), (FilterOperator)99));

    [Fact] public void FilterValues_Contains_OversizedCollection_ReturnsFalse() =>
        Assert.False(FilterValues.Contains(new int[257], FilterValue.From(0L)));

    [Fact] public void FilterValues_Contains_Null_ReturnsFalse() =>
        Assert.False(FilterValues.Contains(null, FilterValue.From(1L)));

    [Fact]
    public void FilterValues_Contains_OversizedEnumerable_ReturnsFalse()
    {
        static IEnumerable<int> LargeSeq() { for (int i = 0; i < 300; i++) yield return i; }
        Assert.False(FilterValues.Contains(LargeSeq(), FilterValue.From(299L)));
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

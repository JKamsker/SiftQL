using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterArrayContainsTests
{
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
}

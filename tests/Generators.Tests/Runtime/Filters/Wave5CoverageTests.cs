using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Kernel;
using SiftQL.Parameterized;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class Wave5CoverageTests
{
    #region FilterExpressionHelpers (0% coverage)

    [Fact]
    public void NumberIn_MatchesValue()
    {
        Assert.True(FilterExpressionHelpers.NumberIn(3.14, [1.0, 2.0, 3.14]));
    }

    [Fact]
    public void NumberIn_NoMatch()
    {
        Assert.False(FilterExpressionHelpers.NumberIn(9.0, [1.0, 2.0, 3.0]));
    }

    [Fact]
    public void NumberIn_EmptyArray()
    {
        Assert.False(FilterExpressionHelpers.NumberIn(1.0, []));
    }

    [Fact]
    public void StringIn_MatchesValue()
    {
        Assert.True(FilterExpressionHelpers.StringIn("b", ["a", "b", "c"], false));
    }

    [Fact]
    public void StringIn_NoMatch()
    {
        Assert.False(FilterExpressionHelpers.StringIn("z", ["a", "b"], false));
    }

    [Fact]
    public void StringIn_NullActualWithHasNull()
    {
        Assert.True(FilterExpressionHelpers.StringIn(null, ["a"], true));
    }

    [Fact]
    public void StringIn_NullActualWithoutHasNull()
    {
        Assert.False(FilterExpressionHelpers.StringIn(null, ["a"], false));
    }

    [Fact]
    public void GuidIn_MatchesValue()
    {
        var g = Guid.NewGuid();
        Assert.True(FilterExpressionHelpers.GuidIn(g, [Guid.Empty, g]));
    }

    [Fact]
    public void GuidIn_NoMatch()
    {
        Assert.False(FilterExpressionHelpers.GuidIn(Guid.NewGuid(), [Guid.Empty]));
    }

    [Fact]
    public void EnumIn_MatchesValue()
    {
        Assert.True(FilterExpressionHelpers.EnumIn(2L, [1L, 2L, 3L]));
    }

    [Fact]
    public void EnumIn_NoMatch()
    {
        Assert.False(FilterExpressionHelpers.EnumIn(5L, [1L, 2L, 3L]));
    }

    #endregion

    #region FilterTypedInCompiler (61% coverage)

    [Fact]
    public void CompileBoolean_MatchesTrueAndFalse()
    {
        Func<object, bool?> getter = obj => ((BoolSubject)obj).Active;
        var values = new[] { FilterValue.From(true), FilterValue.From(false) };
        var compiled = FilterTypedInCompiler.CompileBoolean(getter, values);

        Assert.True(compiled(new BoolSubject(true)));
        Assert.True(compiled(new BoolSubject(false)));
    }

    [Fact]
    public void CompileBoolean_NullWithNullInValues()
    {
        Func<object, bool?> getter = _ => null;
        var values = new[] { FilterValue.From(true), FilterValue.Null };
        var compiled = FilterTypedInCompiler.CompileBoolean(getter, values);

        Assert.True(compiled(new BoolSubject(false)));
    }

    [Fact]
    public void CompileBoolean_NullWithoutNullInValues()
    {
        Func<object, bool?> getter = _ => null;
        var values = new[] { FilterValue.From(true) };
        var compiled = FilterTypedInCompiler.CompileBoolean(getter, values);

        Assert.False(compiled(new BoolSubject(false)));
    }

    [Fact]
    public void CompileNumber_SmallSetUsesUnrolled()
    {
        Func<object, double?> getter = obj => ((NumSubject)obj).Value;
        var values = new[] { FilterValue.From(1.0), FilterValue.From(2.0) };
        var compiled = FilterTypedInCompiler.CompileNumber(getter, values);

        Assert.True(compiled(new NumSubject(1.0)));
        Assert.True(compiled(new NumSubject(2.0)));
        Assert.False(compiled(new NumSubject(3.0)));
    }

    [Fact]
    public void CompileNumber_LargeSetUsesHashSet()
    {
        Func<object, double?> getter = obj => ((NumSubject)obj).Value;
        var values = new[]
        {
            FilterValue.From(1.0), FilterValue.From(2.0),
            FilterValue.From(3.0), FilterValue.From(4.0),
            FilterValue.From(5.0),
        };
        var compiled = FilterTypedInCompiler.CompileNumber(getter, values);

        Assert.True(compiled(new NumSubject(5.0)));
        Assert.False(compiled(new NumSubject(6.0)));
    }

    [Fact]
    public void CompileNumber_NullActualWithNullInValues()
    {
        Func<object, double?> getter = _ => null;
        var values = new[] { FilterValue.From(1.0), FilterValue.Null };
        var compiled = FilterTypedInCompiler.CompileNumber(getter, values);

        Assert.True(compiled(new NumSubject(0)));
    }

    [Fact]
    public void CompileNumber_IntegerValues()
    {
        Func<object, double?> getter = obj => ((NumSubject)obj).Value;
        var values = new[] { FilterValue.From(10L), FilterValue.From(20L) };
        var compiled = FilterTypedInCompiler.CompileNumber(getter, values);

        Assert.True(compiled(new NumSubject(10.0)));
        Assert.False(compiled(new NumSubject(15.0)));
    }

    [Fact]
    public void CompileNumber_DecimalValues()
    {
        Func<object, double?> getter = obj => ((NumSubject)obj).Value;
        var values = new[] { FilterValue.From(1.5m), FilterValue.From(2.5m) };
        var compiled = FilterTypedInCompiler.CompileNumber(getter, values);

        Assert.True(compiled(new NumSubject(1.5)));
    }

    [Fact]
    public void CompileString_SmallSetMatchesOrdinal()
    {
        Func<object, string?> getter = obj => ((StrSubject)obj).Name;
        var values = new[] { FilterValue.From("a"), FilterValue.From("b") };
        var compiled = FilterTypedInCompiler.CompileString(getter, values);

        Assert.True(compiled(new StrSubject("a")));
        Assert.False(compiled(new StrSubject("c")));
    }

    [Fact]
    public void CompileString_LargeSetUsesHashSet()
    {
        Func<object, string?> getter = obj => ((StrSubject)obj).Name;
        var values = new[]
        {
            FilterValue.From("a"), FilterValue.From("b"),
            FilterValue.From("c"), FilterValue.From("d"),
            FilterValue.From("e"),
        };
        var compiled = FilterTypedInCompiler.CompileString(getter, values);

        Assert.True(compiled(new StrSubject("e")));
        Assert.False(compiled(new StrSubject("f")));
    }

    [Fact]
    public void CompileString_NullActualWithNullInValues()
    {
        Func<object, string?> getter = _ => null;
        var values = new[] { FilterValue.From("a"), FilterValue.Null };
        var compiled = FilterTypedInCompiler.CompileString(getter, values);

        Assert.True(compiled(new StrSubject("")));
    }

    [Fact]
    public void CompileString_NullActualWithoutNullInValues()
    {
        Func<object, string?> getter = _ => null;
        var values = new[] { FilterValue.From("a") };
        var compiled = FilterTypedInCompiler.CompileString(getter, values);

        Assert.False(compiled(new StrSubject("")));
    }

    [Fact]
    public void CompileGuid_SmallSetUnrolled()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        Func<object, Guid?> getter = obj => ((GuidSubject)obj).Token;
        var values = new[] { FilterValue.From(g1), FilterValue.From(g2) };
        var compiled = FilterTypedInCompiler.CompileGuid(getter, values);

        Assert.True(compiled(new GuidSubject(g1)));
        Assert.False(compiled(new GuidSubject(Guid.Empty)));
    }

    [Fact]
    public void CompileGuid_LargeSetUsesHashSet()
    {
        var guids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        Func<object, Guid?> getter = obj => ((GuidSubject)obj).Token;
        var values = guids.Select(FilterValue.From).ToArray();
        var compiled = FilterTypedInCompiler.CompileGuid(getter, values);

        Assert.True(compiled(new GuidSubject(guids[4])));
        Assert.False(compiled(new GuidSubject(Guid.Empty)));
    }

    [Fact]
    public void CompileGuid_NullActualWithNullInValues()
    {
        Func<object, Guid?> getter = _ => null;
        var values = new[] { FilterValue.From(Guid.NewGuid()), FilterValue.Null };
        var compiled = FilterTypedInCompiler.CompileGuid(getter, values);

        Assert.True(compiled(new GuidSubject(Guid.Empty)));
    }

    [Fact]
    public void CompileEnum_SmallSetUnrolled()
    {
        Func<object, long?> getter = obj => (long)((EnumSubject2)obj).Kind;
        var values = new[] { FilterValue.From(0L), FilterValue.From(1L) };
        var compiled = FilterTypedInCompiler.CompileEnum(getter, values);

        Assert.True(compiled(new EnumSubject2(TestKind.A)));
        Assert.False(compiled(new EnumSubject2(TestKind.C)));
    }

    [Fact]
    public void CompileEnum_LargeSetUsesHashSet()
    {
        Func<object, long?> getter = obj => (long)((EnumSubject2)obj).Kind;
        var values = Enumerable.Range(0, 5).Select(i => FilterValue.From((long)i)).ToArray();
        var compiled = FilterTypedInCompiler.CompileEnum(getter, values);

        Assert.True(compiled(new EnumSubject2(TestKind.C)));
        Assert.False(compiled(new EnumSubject2((TestKind)99)));
    }

    [Fact]
    public void CompileEnum_NullActualWithNullInValues()
    {
        Func<object, long?> getter = _ => null;
        var values = new[] { FilterValue.From(0L), FilterValue.Null };
        var compiled = FilterTypedInCompiler.CompileEnum(getter, values);

        Assert.True(compiled(new EnumSubject2(TestKind.A)));
    }

    [Fact]
    public void CompileNumber_LargeSetNullActual()
    {
        Func<object, double?> getter = _ => null;
        var values = Enumerable.Range(0, 5).Select(i => FilterValue.From((double)i)).ToArray();
        var compiled = FilterTypedInCompiler.CompileNumber(getter, values);
        Assert.False(compiled(new NumSubject(0)));
    }

    [Fact]
    public void CompileString_LargeSetNullActual()
    {
        Func<object, string?> getter = _ => null;
        var values = Enumerable.Range(0, 5).Select(i => FilterValue.From($"v{i}")).ToArray();
        var compiled = FilterTypedInCompiler.CompileString(getter, values);
        Assert.False(compiled(new StrSubject("")));
    }

    [Fact]
    public void CompileGuid_LargeSetNullActual()
    {
        Func<object, Guid?> getter = _ => null;
        var values = Enumerable.Range(0, 5).Select(_ => FilterValue.From(Guid.NewGuid())).ToArray();
        var compiled = FilterTypedInCompiler.CompileGuid(getter, values);
        Assert.False(compiled(new GuidSubject(Guid.Empty)));
    }

    [Fact]
    public void CompileEnum_LargeSetNullActual()
    {
        Func<object, long?> getter = _ => null;
        var values = Enumerable.Range(0, 5).Select(i => FilterValue.From((long)i)).ToArray();
        var compiled = FilterTypedInCompiler.CompileEnum(getter, values);
        Assert.False(compiled(new EnumSubject2(TestKind.A)));
    }

    #endregion

    #region FilterIndexValue (63% coverage)

    [Theory]
    [InlineData(typeof(byte), (byte)42)]
    [InlineData(typeof(sbyte), (sbyte)-1)]
    [InlineData(typeof(short), (short)100)]
    [InlineData(typeof(ushort), (ushort)200)]
    [InlineData(typeof(int), 300)]
    [InlineData(typeof(uint), 400u)]
    public void TryCreateActual_SmallIntegerTypes(Type _, object value)
    {
        Assert.True(FilterIndexValue.TryCreateActual(value, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
    }

    [Fact]
    public void TryCreateActual_Long()
    {
        Assert.True(FilterIndexValue.TryCreateActual(42L, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
        Assert.Equal(42L, key.Integer);
    }

    [Fact]
    public void TryCreateActual_ULongWithinLongRange()
    {
        Assert.True(FilterIndexValue.TryCreateActual(42UL, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
    }

    [Fact]
    public void TryCreateActual_ULongBeyondLongRange()
    {
        ulong big = (ulong)long.MaxValue + 1;
        Assert.True(FilterIndexValue.TryCreateActual(big, out var key));
        Assert.Equal(FilterValueKind.UnsignedInteger, key.Kind);
        Assert.Equal(big, key.UnsignedInteger);
    }

    [Fact]
    public void TryCreateActual_Float()
    {
        Assert.True(FilterIndexValue.TryCreateActual(1.5f, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact]
    public void TryCreateActual_Double()
    {
        Assert.True(FilterIndexValue.TryCreateActual(3.14, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact]
    public void TryCreateActual_Decimal()
    {
        Assert.True(FilterIndexValue.TryCreateActual(9.99m, out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact]
    public void TryCreateActual_String()
    {
        Assert.True(FilterIndexValue.TryCreateActual("hello", out var key));
        Assert.Equal(FilterValueKind.String, key.Kind);
        Assert.Equal("hello", key.String);
    }

    [Fact]
    public void TryCreateActual_Guid()
    {
        var g = Guid.NewGuid();
        Assert.True(FilterIndexValue.TryCreateActual(g, out var key));
        Assert.Equal(FilterValueKind.Guid, key.Kind);
        Assert.Equal(g, key.Guid);
    }

    [Fact]
    public void TryCreateActual_Bool()
    {
        Assert.True(FilterIndexValue.TryCreateActual(true, out var key));
        Assert.Equal(FilterValueKind.Boolean, key.Kind);
        Assert.True(key.Boolean);
    }

    [Fact]
    public void TryCreateActual_Enum()
    {
        Assert.True(FilterIndexValue.TryCreateActual(TestKind.B, out var key));
        Assert.Equal(FilterValueKind.Integer, key.Kind);
    }

    [Fact]
    public void TryCreateActual_NullReturnsFalse()
    {
        Assert.False(FilterIndexValue.TryCreateActual(null, out _));
    }

    [Fact]
    public void TryCreateActual_UnsupportedTypeReturnsFalse()
    {
        Assert.False(FilterIndexValue.TryCreateActual(DateTime.Now, out _));
    }

    [Fact]
    public void TryCreate_AllValueKinds()
    {
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(true), out var k1));
        Assert.Equal(FilterValueKind.Boolean, k1.Kind);

        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(42L), out var k2));
        Assert.Equal(FilterValueKind.Integer, k2.Kind);

        ulong bigUnsigned = (ulong)long.MaxValue + 1;
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(bigUnsigned), out var k3));
        Assert.Equal(FilterValueKind.UnsignedInteger, k3.Kind);

        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(3.14), out var k4));
        Assert.Equal(FilterValueKind.Number, k4.Kind);

        Assert.True(FilterIndexValue.TryCreate(FilterValue.From("test"), out var k5));
        Assert.Equal(FilterValueKind.String, k5.Kind);

        var g = Guid.NewGuid();
        Assert.True(FilterIndexValue.TryCreate(FilterValue.From(g), out var k6));
        Assert.Equal(FilterValueKind.Guid, k6.Kind);
    }

    [Fact]
    public void TryCreate_NullValueReturnsFalse()
    {
        Assert.False(FilterIndexValue.TryCreate(FilterValue.Null, out _));
    }

    [Fact]
    public void TryCreateActual_WithAccessor_BooleanRequired()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Boolean,
            requiredBoolean: obj => ((BoolSubject)obj).Active);
        Assert.True(FilterIndexValue.TryCreateActual(accessor, new BoolSubject(true), out var key));
        Assert.True(key.Boolean);
    }

    [Fact]
    public void TryCreateActual_WithAccessor_NumberRequired()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Number,
            requiredNumber: obj => ((NumSubject)obj).Value);
        Assert.True(FilterIndexValue.TryCreateActual(accessor, new NumSubject(5.0), out var key));
        Assert.Equal(FilterValueKind.Number, key.Kind);
    }

    [Fact]
    public void TryCreateActual_WithAccessor_String()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.String,
            text: obj => ((StrSubject)obj).Name);
        Assert.True(FilterIndexValue.TryCreateActual(accessor, new StrSubject("hi"), out var key));
        Assert.Equal("hi", key.String);
    }

    [Fact]
    public void TryCreateActual_WithAccessor_StringNull()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.String,
            text: _ => null);
        Assert.False(FilterIndexValue.TryCreateActual(accessor, new StrSubject(""), out _));
    }

    [Fact]
    public void TryCreateActual_WithAccessor_GuidRequired()
    {
        var g = Guid.NewGuid();
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Guid,
            requiredGuid: _ => g);
        Assert.True(FilterIndexValue.TryCreateActual(accessor, new object(), out var key));
        Assert.Equal(g, key.Guid);
    }

    [Fact]
    public void TryCreateActual_WithAccessor_EnumRequired()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Enum,
            requiredEnumeration: _ => 42L);
        Assert.True(FilterIndexValue.TryCreateActual(accessor, new object(), out var key));
        Assert.Equal(42L, key.Integer);
    }

    [Fact]
    public void TryCreateActual_WithAccessor_BooleanNullable()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Boolean,
            boolean: _ => null);
        Assert.False(FilterIndexValue.TryCreateActual(accessor, new object(), out _));
    }

    [Fact]
    public void TryCreateActual_WithAccessor_NumberNullable()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Number,
            number: _ => null);
        Assert.False(FilterIndexValue.TryCreateActual(accessor, new object(), out _));
    }

    [Fact]
    public void TryCreateActual_WithAccessor_GuidNullable()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Guid,
            guid: _ => null);
        Assert.False(FilterIndexValue.TryCreateActual(accessor, new object(), out _));
    }

    [Fact]
    public void TryCreateActual_WithAccessor_EnumNullable()
    {
        var accessor = new FilterScalarAccessor(
            FilterScalarKind.Enum,
            enumeration: _ => null);
        Assert.False(FilterIndexValue.TryCreateActual(accessor, new object(), out _));
    }

    #endregion

    #region FilterNumeric (74% coverage)

    [Theory]
    [InlineData(typeof(sbyte), true)]
    [InlineData(typeof(short), true)]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(long), true)]
    [InlineData(typeof(string), false)]
    public void IsSignedIntegral_ReturnsExpected(Type type, bool expected)
    {
        Assert.Equal(expected, FilterNumeric.IsSignedIntegral(type));
    }

    [Theory]
    [InlineData(typeof(byte), true)]
    [InlineData(typeof(ushort), true)]
    [InlineData(typeof(uint), true)]
    [InlineData(typeof(ulong), true)]
    [InlineData(typeof(int), false)]
    public void IsUnsignedIntegral_ReturnsExpected(Type type, bool expected)
    {
        Assert.Equal(expected, FilterNumeric.IsUnsignedIntegral(type));
    }

    [Theory]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(ulong), true)]
    [InlineData(typeof(decimal), true)]
    [InlineData(typeof(double), false)]
    [InlineData(typeof(float), false)]
    public void IsExactNumeric_ReturnsExpected(Type type, bool expected)
    {
        Assert.Equal(expected, FilterNumeric.IsExactNumeric(type));
    }

    [Fact]
    public void TryDoubleToDecimal_NaN()
    {
        Assert.False(FilterNumeric.TryDoubleToDecimal(double.NaN, out _));
    }

    [Fact]
    public void TryDoubleToDecimal_Infinity()
    {
        Assert.False(FilterNumeric.TryDoubleToDecimal(double.PositiveInfinity, out _));
    }

    [Fact]
    public void TryDoubleToDecimal_Overflow()
    {
        Assert.False(FilterNumeric.TryDoubleToDecimal(double.MaxValue, out _));
    }

    [Fact]
    public void TryDoubleToDecimal_Normal()
    {
        Assert.True(FilterNumeric.TryDoubleToDecimal(1.5, out decimal result));
        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void TryNumberDecimal_Integer()
    {
        Assert.True(FilterNumeric.TryNumberDecimal(FilterValue.From(42L), out decimal result));
        Assert.Equal(42m, result);
    }

    [Fact]
    public void TryNumberDecimal_UnsignedInteger()
    {
        Assert.True(FilterNumeric.TryNumberDecimal(FilterValue.From(42UL), out decimal result));
        Assert.Equal(42m, result);
    }

    [Fact]
    public void TryNumberDecimal_Decimal()
    {
        Assert.True(FilterNumeric.TryNumberDecimal(FilterValue.From(1.5m), out decimal result));
        Assert.Equal(1.5m, result);
    }

    [Fact]
    public void TryNumberDecimal_Number()
    {
        Assert.True(FilterNumeric.TryNumberDecimal(FilterValue.From(3.14), out decimal result));
        Assert.Equal((decimal)3.14, result);
    }

    [Fact]
    public void TryNumberDecimal_StringReturnsFalse()
    {
        Assert.False(FilterNumeric.TryNumberDecimal(FilterValue.From("text"), out _));
    }

    [Fact]
    public void TryDoubleToUInt64_Negative()
    {
        Assert.False(FilterNumeric.TryDoubleToUInt64(-1.0, out _));
    }

    [Fact]
    public void TryDoubleToUInt64_Fractional()
    {
        Assert.False(FilterNumeric.TryDoubleToUInt64(1.5, out _));
    }

    [Fact]
    public void TryDoubleToUInt64_Valid()
    {
        Assert.True(FilterNumeric.TryDoubleToUInt64(42.0, out ulong result));
        Assert.Equal(42UL, result);
    }

    [Fact]
    public void TryDoubleToInt64_Fractional()
    {
        Assert.False(FilterNumeric.TryDoubleToInt64(1.5, out _));
    }

    [Fact]
    public void TryDoubleToInt64_Valid()
    {
        Assert.True(FilterNumeric.TryDoubleToInt64(42.0, out long result));
        Assert.Equal(42L, result);
    }

    [Fact]
    public void TryExactDecimal_AllIntTypes()
    {
        Assert.True(FilterNumeric.TryExactDecimal((byte)1, out _));
        Assert.True(FilterNumeric.TryExactDecimal((sbyte)-1, out _));
        Assert.True(FilterNumeric.TryExactDecimal((short)100, out _));
        Assert.True(FilterNumeric.TryExactDecimal((ushort)200, out _));
        Assert.True(FilterNumeric.TryExactDecimal(300, out _));
        Assert.True(FilterNumeric.TryExactDecimal(400u, out _));
        Assert.True(FilterNumeric.TryExactDecimal(500L, out _));
        Assert.True(FilterNumeric.TryExactDecimal(600UL, out _));
        Assert.True(FilterNumeric.TryExactDecimal(1.5m, out var r));
        Assert.Equal(1.5m, r);
    }

    [Fact]
    public void TryExactDecimal_UnsupportedType()
    {
        Assert.False(FilterNumeric.TryExactDecimal(1.5, out _));
        Assert.False(FilterNumeric.TryExactDecimal("nope", out _));
        Assert.False(FilterNumeric.TryExactDecimal(null, out _));
    }

    #endregion

    #region FilterTypedPredicates (77% coverage)

    [Fact]
    public void CompileCompare_NumberNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 5.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberNotEqual()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 5.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberGreaterThan()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 10.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(5.0), FilterOperator.GreaterThan);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberGreaterThanOrEqual()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 5.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(5.0), FilterOperator.GreaterThanOrEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberLessThan()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 3.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(5.0), FilterOperator.LessThan);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberLessThanOrEqual()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 5.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(5.0), FilterOperator.LessThanOrEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberIntegerValue()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 42.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(42L), FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberUnsignedIntegerValue()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 42.0);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(42UL), FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NumberDecimalValue()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 1.5);
        var field = CreateField("Score", typeof(double), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(1.5m), FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_BooleanNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Boolean, boolean: _ => true);
        var field = CreateField("Active", typeof(bool), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompileCompare_BooleanNotEqualNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Boolean, boolean: _ => true);
        var field = CreateField("Active", typeof(bool), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_GuidNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Guid, guid: _ => Guid.NewGuid());
        var field = CreateField("Token", typeof(Guid), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompileCompare_GuidNotEqualNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Guid, guid: _ => Guid.NewGuid());
        var field = CreateField("Token", typeof(Guid), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_GuidNotEqual()
    {
        var g = Guid.NewGuid();
        var scalar = new FilterScalarAccessor(FilterScalarKind.Guid, guid: _ => g);
        var field = CreateField("Token", typeof(Guid), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(Guid.Empty), FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_EnumNonIntegerReturnsNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Enum, enumeration: _ => 0L);
        var field = CreateField("Kind", typeof(TestKind), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From("A"), FilterOperator.Equal);
        Assert.Null(pred);
    }

    [Fact]
    public void CompileCompare_EnumNotEqual()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Enum, enumeration: _ => 0L);
        var field = CreateField("Kind", typeof(TestKind), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(1L), FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_StringNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.String, text: _ => "hello");
        var field = CreateField("Name", typeof(string), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.Null, FilterOperator.Equal);
        Assert.NotNull(pred);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompileCompare_StringNotEqual()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.String, text: _ => "a");
        var field = CreateField("Name", typeof(string), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From("b"), FilterOperator.NotEqual);
        Assert.NotNull(pred);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompileCompare_NullScalarReturnsNull()
    {
        var field = CreateField("Name", typeof(string), null);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From("a"), FilterOperator.Equal);
        Assert.Null(pred);
    }

    [Fact]
    public void CompileIn_EnumWithStringReturnsNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Enum, enumeration: _ => 0L);
        var field = CreateField("Kind", typeof(TestKind), scalar);
        var pred = FilterTypedPredicates.TryCompileIn(field, [FilterValue.From("A")]);
        Assert.Null(pred);
    }

    [Fact]
    public void CompileIn_NullScalarReturnsNull()
    {
        var field = CreateField("Name", typeof(string), null);
        var pred = FilterTypedPredicates.TryCompileIn(field, [FilterValue.From("a")]);
        Assert.Null(pred);
    }

    [Fact]
    public void CompileCompare_NumberNotDoubleTypeReturnsNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 42.0);
        var field = CreateField("Amount", typeof(decimal), scalar);
        var pred = FilterTypedPredicates.TryCompileCompare(field, FilterValue.From(42L), FilterOperator.Equal);
        Assert.Null(pred);
    }

    [Fact]
    public void CompileIn_NumberNotDoubleTypeReturnsNull()
    {
        var scalar = new FilterScalarAccessor(FilterScalarKind.Number, number: _ => 42.0);
        var field = CreateField("Amount", typeof(decimal), scalar);
        var pred = FilterTypedPredicates.TryCompileIn(field, [FilterValue.From(42L)]);
        Assert.Null(pred);
    }

    #endregion

    #region End-to-end: In filters via FilterCompiler for uncovered branches

    [Fact]
    public void InFilter_WithBooleanValues()
    {
        var filter = FilterExpression.In(
            nameof(BoolFilterSubject.Active),
            [FilterValue.From(true)]);
        var kernel = FilterCompiler.Compile(typeof(BoolFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new BoolFilterSubject(true)));
        Assert.False(kernel.Matches(new BoolFilterSubject(false)));
    }

    [Fact]
    public void InFilter_WithGuidValues()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var filter = FilterExpression.In(
            nameof(GuidFilterSubject.Token),
            [FilterValue.From(g1), FilterValue.From(g2)]);
        var kernel = FilterCompiler.Compile(typeof(GuidFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new GuidFilterSubject(g1)));
        Assert.True(kernel.Matches(new GuidFilterSubject(g2)));
        Assert.False(kernel.Matches(new GuidFilterSubject(Guid.Empty)));
    }

    [Fact]
    public void InFilter_WithStringAndNull()
    {
        var filter = FilterExpression.In(
            nameof(StrFilterSubject.Name),
            [FilterValue.From("hello"), FilterValue.Null]);
        var kernel = FilterCompiler.Compile(typeof(StrFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new StrFilterSubject("hello")));
        Assert.True(kernel.Matches(new StrFilterSubject(null)));
        Assert.False(kernel.Matches(new StrFilterSubject("world")));
    }

    [Fact]
    public void InFilter_WithLargeStringSet()
    {
        var values = Enumerable.Range(0, 5).Select(i => FilterValue.From($"val{i}")).ToArray();
        var filter = FilterExpression.In(nameof(StrFilterSubject.Name), values);
        var kernel = FilterCompiler.Compile(typeof(StrFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new StrFilterSubject("val3")));
        Assert.False(kernel.Matches(new StrFilterSubject("other")));
    }

    [Fact]
    public void InFilter_WithUnsignedIntegerValues()
    {
        var filter = FilterExpression.In(
            nameof(UIntFilterSubject.Value),
            [FilterValue.From(10L), FilterValue.From(20L)]);
        var kernel = FilterCompiler.Compile(typeof(UIntFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new UIntFilterSubject(10)));
        Assert.False(kernel.Matches(new UIntFilterSubject(30)));
    }

    [Fact]
    public void CompareFilter_UnsignedIntegerNegativeValue()
    {
        var filter = FilterExpression.Compare(
            nameof(UIntFilterSubject.Value),
            FilterOperator.Equal,
            FilterValue.From(-1L));
        var kernel = FilterCompiler.Compile(typeof(UIntFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.False(kernel.Matches(new UIntFilterSubject(0)));
    }

    [Fact]
    public void CompareFilter_UnsignedIntegerGreaterThanNegative()
    {
        var filter = FilterExpression.Compare(
            nameof(UIntFilterSubject.Value),
            FilterOperator.GreaterThan,
            FilterValue.From(-1L));
        var kernel = FilterCompiler.Compile(typeof(UIntFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new UIntFilterSubject(0)));
    }

    [Fact]
    public void CompareFilter_DecimalWithIntegerValue()
    {
        var filter = FilterExpression.Compare(
            nameof(DecFilterSubject.Amount),
            FilterOperator.Equal,
            FilterValue.From(42L));
        var kernel = FilterCompiler.Compile(typeof(DecFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new DecFilterSubject(42m)));
        Assert.False(kernel.Matches(new DecFilterSubject(43m)));
    }

    [Fact]
    public void CompareFilter_UnsignedIntegerUnsignedValue()
    {
        var filter = FilterExpression.Compare(
            nameof(UIntFilterSubject.Value),
            FilterOperator.Equal,
            FilterValue.From(10UL));
        var kernel = FilterCompiler.Compile(typeof(UIntFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new UIntFilterSubject(10)));
    }

    [Fact]
    public void CompareFilter_SignedWithLargeUnsigned()
    {
        ulong big = (ulong)long.MaxValue + 1;
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(big));
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Immediate);
        Assert.False(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    [Fact]
    public void CompareFilter_SignedWithLargeUnsigned_LessThan()
    {
        ulong big = (ulong)long.MaxValue + 1;
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.LessThan,
            FilterValue.From(big));
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    [Fact]
    public void CompareFilter_DecimalWithUnsigned()
    {
        var filter = FilterExpression.Compare(
            nameof(DecFilterSubject.Amount),
            FilterOperator.Equal,
            FilterValue.From(42UL));
        var kernel = FilterCompiler.Compile(typeof(DecFilterSubject), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new DecFilterSubject(42m)));
    }

    [Fact]
    public void CompareFilter_IntWithDecimalValue()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(42.0m));
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    [Fact]
    public void CompareFilter_IntWithDoubleValue()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(42.0));
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    [Fact]
    public void CompareFilter_IntWithNaNDouble()
    {
        var filter = FilterExpression.Compare(
            nameof(ItemUsedEvent.ItemId),
            FilterOperator.Equal,
            FilterValue.From(double.NaN));
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), filter, FilterCompilerOptions.Immediate);
        Assert.False(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    #endregion

    #region KernelExpressionEvaluator static property/field symmetry

    [Fact]
    public void QueryKernel_StaticField_Succeeds()
    {
        var query = QueryKernel.For<ItemUsedEvent>()
            .Where(e => e.ItemId == (int)StaticTestValues.FieldValue);
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), query.Filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
        Assert.False(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 99, 1)));
    }

    [Fact]
    public void QueryKernel_StaticProperty_Succeeds()
    {
        var query = QueryKernel.For<ItemUsedEvent>()
            .Where(e => e.ItemId == (int)StaticTestValues.PropertyValue);
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), query.Filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
        Assert.False(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 99, 1)));
    }

    [Fact]
    public void QueryKernel_InstanceField_Succeeds()
    {
        var holder = new ValueHolder { Value = 42L };
        var query = QueryKernel.For<ItemUsedEvent>()
            .Where(e => e.ItemId == (int)holder.Value);
        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), query.Filter, FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
    }

    #endregion

    #region ParameterizedFilterPlan nodes

    [Fact]
    public void ConstantFilterPlanNode_True()
    {
        var node = new ConstantFilterPlanNode(true);
        var pred = node.Bind([]);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void ConstantFilterPlanNode_False()
    {
        var node = new ConstantFilterPlanNode(false);
        var pred = node.Bind([]);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompositeFilterPlanNode_AndAllMatch()
    {
        var children = new ParameterizedFilterPlanNode[]
        {
            new ConstantFilterPlanNode(true),
            new ConstantFilterPlanNode(true),
        };
        var node = new CompositeFilterPlanNode(children, and: true);
        var pred = node.Bind([]);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompositeFilterPlanNode_AndOneFails()
    {
        var children = new ParameterizedFilterPlanNode[]
        {
            new ConstantFilterPlanNode(true),
            new ConstantFilterPlanNode(false),
        };
        var node = new CompositeFilterPlanNode(children, and: true);
        var pred = node.Bind([]);
        Assert.False(pred(new object()));
    }

    [Fact]
    public void CompositeFilterPlanNode_OrOneMatches()
    {
        var children = new ParameterizedFilterPlanNode[]
        {
            new ConstantFilterPlanNode(false),
            new ConstantFilterPlanNode(true),
        };
        var node = new CompositeFilterPlanNode(children, and: false);
        var pred = node.Bind([]);
        Assert.True(pred(new object()));
    }

    [Fact]
    public void CompositeFilterPlanNode_OrNoneMatch()
    {
        var children = new ParameterizedFilterPlanNode[]
        {
            new ConstantFilterPlanNode(false),
            new ConstantFilterPlanNode(false),
        };
        var node = new CompositeFilterPlanNode(children, and: false);
        var pred = node.Bind([]);
        Assert.False(pred(new object()));
    }

    #endregion

    #region Helpers

    private static FilterField CreateField(string name, Type valueType, FilterScalarAccessor? scalar)
    {
        return new FilterField(
            name,
            valueType,
            FilterFieldKind.Scalar,
            _ => null,
            ScalarAccessor: scalar,
            ArrayAccessor: null);
    }

    private sealed record BoolSubject(bool Active);
    private sealed record NumSubject(double Value);
    private sealed record StrSubject(string? Name);
    private sealed record GuidSubject(Guid Token);
    private sealed record EnumSubject2(TestKind Kind);

    private sealed record BoolFilterSubject(bool Active) : IFilterSubject;
    private sealed record GuidFilterSubject(Guid Token) : IFilterSubject;
    private sealed record StrFilterSubject(string? Name) : IFilterSubject;
    private sealed record UIntFilterSubject(uint Value) : IFilterSubject;
    private sealed record DecFilterSubject(decimal Amount) : IFilterSubject;

    internal enum TestKind { A, B, C }

    internal static class StaticTestValues
    {
        public static readonly long FieldValue = 42L;
        public static long PropertyValue => 42L;
    }

    internal class ValueHolder
    {
        public long Value;
    }

    #endregion
}

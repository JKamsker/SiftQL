using SiftQL.Expressions;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterTypedInCompilerTests
{
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

    private sealed record BoolSubject(bool Active);
    private sealed record NumSubject(double Value);
    private sealed record StrSubject(string? Name);
    private sealed record GuidSubject(Guid Token);
    private sealed record EnumSubject2(TestKind Kind);

    internal enum TestKind { A, B, C }
}

using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterInTests
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
}

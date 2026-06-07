using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ParameterizedFilterPlanNodeTests
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
}

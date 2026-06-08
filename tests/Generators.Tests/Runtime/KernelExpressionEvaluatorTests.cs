using SiftQL;
using SiftQL.Compiler;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelExpressionEvaluatorTests
{
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

    [Fact]
    public void QueryKernel_ConstantSideNarrowingCast_UsesConvertedValue()
    {
        var holder = new ValueHolder { Value = 4_294_967_297L };
        var query = QueryKernel.For<ItemUsedEvent>()
            .Where(e => e.ItemId == (int)holder.Value);

        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 1, 1)));
        Assert.False(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 2, 1)));
    }

    [Fact]
    public void QueryKernel_FieldSideNarrowingCast_IsRejected()
    {
        Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<ItemUsedEvent>()
                .Where(e => (int)e.CharacterId == 1));
    }

    [Fact]
    public void QueryKernel_StaticFieldOnLeftSide_Succeeds()
    {
        var query = QueryKernel.For<ItemUsedEvent>()
            .Where(e => StaticTestValues.FieldValue <= e.ItemId);

        var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), query.Filter, FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 42, 1)));
        Assert.False(kernel.Matches(new ItemUsedEvent(Guid.Empty, 1, 41, 1)));
    }

    internal static class StaticTestValues
    {
        public static readonly long FieldValue = 42L;
        public static long PropertyValue => 42L;
    }

    internal class ValueHolder
    {
        public long Value;
    }
}

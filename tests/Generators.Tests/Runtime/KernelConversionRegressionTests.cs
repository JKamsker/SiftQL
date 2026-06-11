using SiftQL.Compiler;
using SiftQL.Translation;

namespace SiftQL.Generators.Tests;

public sealed class KernelConversionRegressionTests
{
    [Fact]
    public void ExplicitEnumConstantConversionUsesConvertedNumericValue()
    {
        var holder = new EnumHolder { Kind = ItemKind.Rare };
        QueryKernel<EnumConversionEvent> query = QueryKernel.For<EnumConversionEvent>()
            .Where(ev => ev.ItemId == (long)holder.Kind);
        var kernel = FilterCompiler.Compile(
            typeof(EnumConversionEvent),
            query.Filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new EnumConversionEvent(2)));
        Assert.False(kernel.Matches(new EnumConversionEvent(1)));
    }

    [Fact]
    public void ExactFieldSideNumericWideningToDoubleIsSupported()
    {
        QueryKernel<NumericConversionEvent> query = QueryKernel.For<NumericConversionEvent>()
            .Where(ev => ev.Score == 1.5D && ev.Count == 42D);
        var kernel = FilterCompiler.Compile(
            typeof(NumericConversionEvent),
            query.Filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NumericConversionEvent(1.5F, 42, 42)));
        Assert.False(kernel.Matches(new NumericConversionEvent(1.25F, 42, 42)));
    }

    [Fact]
    public void ExactFieldSideIntegralWideningToDecimalIsSupported()
    {
        QueryKernel<NumericConversionEvent> query = QueryKernel.For<NumericConversionEvent>()
            .Where(ev => ev.Count == 42m && ev.LongCount == 42m);
        var kernel = FilterCompiler.Compile(
            typeof(NumericConversionEvent),
            query.Filter,
            FilterCompilerOptions.Immediate);

        Assert.True(kernel.Matches(new NumericConversionEvent(1.5F, 42, 42)));
        Assert.False(kernel.Matches(new NumericConversionEvent(1.5F, 42, 41)));
    }

    [Fact]
    public void LossyFieldSideNumericWideningToDoubleIsRejected()
    {
        Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<NumericConversionEvent>()
                .Where(ev => ev.LongCount == 42D));
    }

    [Fact]
    public void ContextLossyFieldSideConversionIsRejected()
    {
        Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<NumericConversionEvent, ConversionContext>()
                .Where(static (ev, _) => (byte)ev.Count == (byte)44));
    }

    private enum ItemKind : long
    {
        Common = 1,
        Rare = 2,
    }

    private sealed record EnumHolder
    {
        public ItemKind Kind { get; init; }
    }

    private sealed record EnumConversionEvent(long ItemId) : IFilterSubject;

    private sealed record NumericConversionEvent(
        float Score,
        int Count,
        long LongCount) : IFilterSubject;

    private sealed class ConversionContext;
}

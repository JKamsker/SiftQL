using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Translation;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelCaseInsensitiveTranslationTests
{
    [Fact]
    public void WhereStringEqualsOrdinalIgnoreCaseMatchesAnyCasing()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(static e => string.Equals(e.Name, "eu-west", StringComparison.OrdinalIgnoreCase));

        Assert.True(kernel.Filter.IgnoreCase);
        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("eu-west"), true),
            new FilterCase(new StringSubject("EU-West"), true),
            new FilterCase(new StringSubject("us-east"), false),
            new FilterCase(new StringSubject(null), false));
    }

    [Fact]
    public void WhereInstanceEqualsIgnoreCaseMatches()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(static e => e.Name!.Equals("paid", StringComparison.OrdinalIgnoreCase));

        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("PAID"), true),
            new FilterCase(new StringSubject("paid"), true),
            new FilterCase(new StringSubject("void"), false));
    }

    [Fact]
    public void WhereStartsWithIgnoreCaseMatches()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(static e => e.Name!.StartsWith("orders/", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(FilterOperator.StringStartsWith, kernel.Filter.Operator);
        Assert.True(kernel.Filter.IgnoreCase);
        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("ORDERS/eu"), true),
            new FilterCase(new StringSubject("orders/eu"), true),
            new FilterCase(new StringSubject("events/x"), false));
    }

    [Fact]
    public void WhereContainsIgnoreCaseMatches()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(static e => e.Name!.Contains("ell", StringComparison.OrdinalIgnoreCase));

        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("HELLO"), true),
            new FilterCase(new StringSubject("yellow"), true),
            new FilterCase(new StringSubject("world"), false));
    }

    [Fact]
    public void OrdinalEqualityRemainsCaseSensitive()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(static e => e.Name == "EU");

        Assert.False(kernel.Filter.IgnoreCase);
        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("EU"), true),
            new FilterCase(new StringSubject("eu"), false));
    }

    [Fact]
    public void CultureAwareComparisonIsRejected()
    {
        KernelExpressionException ex = Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<StringSubject>()
                .Where(static e => string.Equals(e.Name, "x", StringComparison.CurrentCultureIgnoreCase)));

        Assert.Contains("OrdinalIgnoreCase", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoreCaseFactoryCompilesCaseInsensitively()
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(StringSubject.Name),
            FilterOperator.Equal,
            FilterValue.From("paid"),
            ignoreCase: true);

        AssertFilter(
            filter,
            new FilterCase(new StringSubject("PAID"), true),
            new FilterCase(new StringSubject("paid"), true),
            new FilterCase(new StringSubject("void"), false));
    }

    [Fact]
    public void FilterValuesCompareHonorsIgnoreCase()
    {
        Assert.True(FilterValues.Compare("EU", FilterValue.From("eu"), FilterOperator.Equal, ignoreCase: true));
        Assert.False(FilterValues.Compare("EU", FilterValue.From("eu"), FilterOperator.Equal));
        Assert.True(FilterValues.Compare("HELLO", FilterValue.From("ell"), FilterOperator.StringContains, ignoreCase: true));
    }

    [Fact]
    public void IgnoreCaseChangesContentSignature()
    {
        var sensitive = FilterExpression.Compare(nameof(StringSubject.Name), FilterOperator.Equal, FilterValue.From("eu"));
        var insensitive = FilterExpression.Compare(nameof(StringSubject.Name), FilterOperator.Equal, FilterValue.From("eu"), ignoreCase: true);

        Assert.NotEqual(
            FilterExpression.ContentSignature(sensitive),
            FilterExpression.ContentSignature(insensitive));
    }

    private static void AssertFilter(FilterExpression filter, params FilterCase[] cases)
    {
        CompiledKernel immediate = FilterCompiler.Compile(typeof(StringSubject), filter, FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(typeof(StringSubject), filter, FilterCompilerOptions.Tiered);
        foreach (FilterCase item in cases)
        {
            Assert.Equal(item.Expected, immediate.Matches(item.Subject));
            Assert.Equal(item.Expected, tiered.Matches(item.Subject));
        }
    }

    private sealed record StringSubject(string? Name) : IFilterSubject;
    private sealed record FilterCase(StringSubject Subject, bool Expected);
}

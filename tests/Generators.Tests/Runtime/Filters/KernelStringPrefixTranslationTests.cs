using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Translation;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelStringPrefixTranslationTests
{
    [Fact]
    public void WhereStartsWithMatchesOrdinalPrefix()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(static subject => subject.Name!.StartsWith("orders/eu/"));

        Assert.Equal(FilterOperator.StringStartsWith, kernel.Filter.Operator);
        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("orders/eu/123"), true),
            new FilterCase(new StringSubject("orders/us/123"), false),
            new FilterCase(new StringSubject("ORDERS/EU/1"), false),
            new FilterCase(new StringSubject(null), false));
    }

    [Fact]
    public void WhereEndsWithMatchesOrdinalSuffix()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(static subject => subject.Name!.EndsWith(".json"));

        Assert.Equal(FilterOperator.StringEndsWith, kernel.Filter.Operator);
        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("report.json"), true),
            new FilterCase(new StringSubject("report.JSON"), false),
            new FilterCase(new StringSubject("report.xml"), false),
            new FilterCase(new StringSubject(null), false));
    }

    [Fact]
    public void WhereStartsWithUsesCapturedPrefixAsParameter()
    {
        string prefix = "itm_";

        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(subject => subject.Name!.StartsWith(prefix));

        Assert.Equal("p0", kernel.Filter.Value?.ParameterKey);
        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("itm_42"), true),
            new FilterCase(new StringSubject("usr_42"), false));
    }

    [Fact]
    public void StringPrefixFactoriesCompileAndMatch()
    {
        AssertFilter(
            FilterExpression.StringStartsWith(nameof(StringSubject.Name), FilterValue.From("a")),
            new FilterCase(new StringSubject("abc"), true),
            new FilterCase(new StringSubject("xyz"), false),
            new FilterCase(new StringSubject(null), false));
        AssertFilter(
            FilterExpression.StringEndsWith(nameof(StringSubject.Name), FilterValue.From("z")),
            new FilterCase(new StringSubject("xyz"), true),
            new FilterCase(new StringSubject("abc"), false),
            new FilterCase(new StringSubject(null), false));
    }

    [Fact]
    public void StringPrefixOperatorsRejectNonStringField()
    {
        var startsWith = FilterExpression.StringStartsWith(
            nameof(StringSubject.Count),
            FilterValue.From("x"));

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(StringSubject), startsWith, FilterCompilerOptions.Immediate));
        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(StringSubject), startsWith, FilterCompilerOptions.Tiered));
    }

    [Fact]
    public void OrdinalStringComparisonOverloadStaysCaseSensitive()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(subject => subject.Name!.StartsWith("x", StringComparison.Ordinal));

        Assert.Equal(FilterOperator.StringStartsWith, kernel.Filter.Operator);
        Assert.False(kernel.Filter.IgnoreCase);
    }

    [Fact]
    public void FilterValuesMatchPrefixAndSuffixDirectly()
    {
        Assert.True(FilterValues.Compare("orders/eu/1", FilterValue.From("orders/"), FilterOperator.StringStartsWith));
        Assert.False(FilterValues.Compare("orders/eu/1", FilterValue.From("ORDERS/"), FilterOperator.StringStartsWith));
        Assert.True(FilterValues.Compare("report.json", FilterValue.From(".json"), FilterOperator.StringEndsWith));
        Assert.False(FilterValues.Compare("report.json", FilterValue.From(".JSON"), FilterOperator.StringEndsWith));
        Assert.False(FilterValues.Compare(null, FilterValue.From("x"), FilterOperator.StringStartsWith));
        Assert.False(FilterValues.Compare(42, FilterValue.From("x"), FilterOperator.StringEndsWith));
    }

    private static void AssertFilter(FilterExpression filter, params FilterCase[] cases)
    {
        CompiledKernel immediate = FilterCompiler.Compile(
            typeof(StringSubject),
            filter,
            FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(
            typeof(StringSubject),
            filter,
            FilterCompilerOptions.Tiered);

        foreach (FilterCase item in cases)
        {
            Assert.Equal(item.Expected, immediate.Matches(item.Subject));
            Assert.Equal(item.Expected, tiered.Matches(item.Subject));
        }
    }

    private sealed record StringSubject(string? Name = null, int Count = 0) : IFilterSubject;
    private sealed record FilterCase(StringSubject Subject, bool Expected);
}

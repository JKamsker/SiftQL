using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelNullOrEmptyTranslationTests
{
    [Fact]
    public void WhereIsNullOrEmptyMatchesNullAndEmpty()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(static subject => string.IsNullOrEmpty(subject.Name));

        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject(null), true),
            new FilterCase(new StringSubject(string.Empty), true),
            new FilterCase(new StringSubject(" "), false),
            new FilterCase(new StringSubject("guild"), false));
    }

    [Fact]
    public void WhereNotIsNullOrEmptyMatchesNonEmpty()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(static subject => !string.IsNullOrEmpty(subject.Name));

        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("guild"), true),
            new FilterCase(new StringSubject(" "), true),
            new FilterCase(new StringSubject(null), false),
            new FilterCase(new StringSubject(string.Empty), false));
    }

    [Fact]
    public void IsNullOrWhiteSpaceThrowsActionableError()
    {
        KernelExpressionException ex = Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<StringSubject>()
                .Where(static subject => string.IsNullOrWhiteSpace(subject.Name)));

        Assert.Contains("whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsNullOrEmpty", ex.Message, StringComparison.Ordinal);
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

    private sealed record StringSubject(string? Name) : IFilterSubject;
    private sealed record FilterCase(StringSubject Subject, bool Expected);
}

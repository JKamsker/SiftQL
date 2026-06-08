using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelStringContainsTranslationTests
{
    [Fact]
    public void WhereStringContainsMatchesOrdinalSubstring()
    {
        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(static subject => subject.Name!.Contains("Hello"));

        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("Say Hello"), true),
            new FilterCase(new StringSubject("say hello"), false),
            new FilterCase(new StringSubject("Other"), false),
            new FilterCase(new StringSubject(null), false));
    }

    [Fact]
    public void WhereStringContainsUsesCapturedSubstringAsParameter()
    {
        string substring = "ell";

        QueryKernel<StringSubject> kernel = QueryKernel.For<StringSubject>()
            .Where(subject => subject.Name!.Contains(substring));

        Assert.Equal("p0", kernel.Filter.Value?.ParameterKey);
        AssertFilter(
            kernel.Filter,
            new FilterCase(new StringSubject("Hello"), true),
            new FilterCase(new StringSubject("Hero"), false));
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

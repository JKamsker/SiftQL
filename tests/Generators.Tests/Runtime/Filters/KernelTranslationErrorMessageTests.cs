using SiftQL;
using SiftQL.Kernel;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelTranslationErrorMessageTests
{
    [Fact]
    public void ArithmeticComparisonExplainsArithmeticIsUnsupported()
    {
        KernelExpressionException ex = Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<Subject>().Where(static e => e.Score + e.Bonus > 100));

        Assert.Contains("Arithmetic", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TernaryExplainsConditionalIsUnsupported()
    {
        KernelExpressionException ex = Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<Subject>().Where(static e => e.Active ? e.Score > 1 : e.Bonus > 1));

        Assert.Contains("Conditional", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedMethodOnComparisonNamesTheMethod()
    {
        KernelExpressionException ex = Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<Subject>().Where(static e => e.Name!.ToLower() == "x"));

        Assert.Contains("ToLower", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryUnsupportedMessageKeepsStableMachinePrefix()
    {
        KernelExpressionException ex = Assert.Throws<KernelExpressionException>(() =>
            QueryKernel.For<Subject>().Where(static e => e.Score + e.Bonus > 100));

        Assert.Contains("Unsupported server kernel expression", ex.Message, StringComparison.Ordinal);
    }

    private sealed record Subject(
        int Score = 0,
        int Bonus = 0,
        bool Active = false,
        string? Name = null) : IFilterSubject;
}

using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterNullableNumericValidationRegressionTests
{
    [Fact]
    public void CustomSchemaNullableNumericFieldSupportsOrderedComparison()
    {
        FilterExpression filter = FilterExpression.Compare(
            nameof(NullableScoreSubject.Score),
            FilterOperator.GreaterThan,
            FilterValue.From(1L));

        CompiledKernel kernel = FilterCompiler.CompileWithSchema(
            typeof(NullableScoreSubject),
            filter,
            FilterCompilerOptions.Immediate,
            errorFactory: null,
            _ => new FilterSchema(
                typeof(NullableScoreSubject),
                [
                    new FilterField(
                        nameof(NullableScoreSubject.Score),
                        typeof(int?),
                        FilterFieldKind.Scalar,
                        static subject => ((NullableScoreSubject)subject).Score),
                ]));

        Assert.True(kernel.Matches(new NullableScoreSubject(2)));
        Assert.False(kernel.Matches(new NullableScoreSubject(null)));
    }

    private sealed record NullableScoreSubject(int? Score) : IFilterSubject;
}

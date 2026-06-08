using SiftQL.Compiler;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaProviderValidationRegressionTests
{
    [Fact]
    public void RegisteredProviderCannotReturnSchemaForDifferentSubject()
    {
        GeneratedFilterSchemaRegistry.Register(
            typeof(ExpectedProviderSubject).Assembly,
            static (Type candidate, out FilterSchema? schema) =>
            {
                if (candidate != typeof(ExpectedProviderSubject))
                {
                    schema = null;
                    return false;
                }

                schema = GeneratedFilterSchemaRegistry.Create(typeof(OtherProviderSubject), []);
                return true;
            });

        Assert.Throws<FilterValidationException>(() =>
            FilterSchema.For(typeof(ExpectedProviderSubject)));
    }

    private sealed record ExpectedProviderSubject(int Id) : IFilterSubject;

    private sealed record OtherProviderSubject(int Id) : IFilterSubject;
}

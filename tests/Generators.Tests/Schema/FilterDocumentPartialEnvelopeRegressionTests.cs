using SiftQL.Compiler;
using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterDocumentPartialEnvelopeRegressionTests
{
    [Fact]
    public void DeserializeRejectsEnvelopeWithVersionButNoFilter()
    {
        FilterSerializationException ex = Assert.Throws<FilterSerializationException>(() =>
            FilterDocument.Deserialize("{\"Version\":1}"));

        Assert.Contains("Filter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatorRejectsPartialEnvelopeJson()
    {
        FilterValidationResult result = FilterValidator.Validate(
            typeof(DocumentSubject),
            "{\"Version\":1}");

        Assert.False(result.IsValid);
        Assert.Contains("Filter", result.Errors.Single().Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DocumentSubject(string Region) : IFilterSubject;
}

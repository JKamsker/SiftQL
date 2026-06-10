using System.Text.Json;
using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterDocumentEnvelopeTests
{
    private static FilterExpression SampleFilter() => FilterExpression.And(
        FilterExpression.Compare("region", FilterOperator.Equal, FilterValue.From("EU")),
        FilterExpression.Compare("total", FilterOperator.GreaterThan, FilterValue.From(100.0)));

    [Fact]
    public void SerializeIncludesVersion()
    {
        string json = FilterDocument.Serialize(SampleFilter());

        using var document = JsonDocument.Parse(json);
        Assert.Equal(FilterDocument.CurrentVersion, document.RootElement.GetProperty("Version").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("Filter", out _));
    }

    [Fact]
    public void RoundTripPreservesFilter()
    {
        FilterExpression original = SampleFilter();

        FilterExpression restored = FilterDocument.Deserialize(FilterDocument.Serialize(original));

        Assert.Equal(
            FilterExpression.ContentSignature(original),
            FilterExpression.ContentSignature(restored));
    }

    [Fact]
    public void ReadsLegacyBareFilterWithoutEnvelope()
    {
        FilterExpression original = SampleFilter();
        string bareJson = JsonSerializer.Serialize(original);

        FilterExpression restored = FilterDocument.Deserialize(bareJson);

        Assert.Equal(
            FilterExpression.ContentSignature(original),
            FilterExpression.ContentSignature(restored));
    }

    [Fact]
    public void RejectsNewerFormatVersion()
    {
        string future = "{\"Version\":999,\"Filter\":{\"Kind\":0}}";

        FilterSerializationException ex = Assert.Throws<FilterSerializationException>(() =>
            FilterDocument.Deserialize(future));

        Assert.Contains("999", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTripsEnvelopeWrittenWithCamelCaseNamingPolicy()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        FilterExpression original = SampleFilter();

        string json = FilterDocument.Serialize(original, options);

        // The envelope really is camelCase, so a case-sensitive PascalCase lookup
        // would miss it -- the reader must honor the naming policy.
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            Assert.True(document.RootElement.TryGetProperty("version", out _));
            Assert.True(document.RootElement.TryGetProperty("filter", out _));
        }

        FilterExpression restored = FilterDocument.Deserialize(json, options);

        Assert.Equal(
            FilterExpression.ContentSignature(original),
            FilterExpression.ContentSignature(restored));
    }

    [Fact]
    public void RecognizesEnvelopeWithCaseInsensitiveOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        FilterExpression restored = FilterDocument.Deserialize(
            "{\"version\":1,\"filter\":{\"Kind\":0}}",
            options);

        Assert.Equal(FilterExpressionKind.Any, restored.Kind);
    }
}

using System.Linq;
using System.Text.Json;
using SiftQL;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaDescriptorTests
{
    [Fact]
    public void DescribeListsFieldsWithTypeAppropriateOperators()
    {
        FilterSchemaDescriptor descriptor = FilterSchema.For(typeof(DescribedEvent)).Describe();

        FilterFieldDescriptor name = Field(descriptor, nameof(DescribedEvent.Name));
        Assert.Equal(FilterFieldKind.Scalar, name.Kind);
        Assert.Contains("StringStartsWith", name.Operators);
        Assert.Contains("StringEndsWith", name.Operators);
        Assert.Contains("Equal", name.Operators);
        Assert.DoesNotContain("GreaterThan", name.Operators);

        FilterFieldDescriptor score = Field(descriptor, nameof(DescribedEvent.Score));
        Assert.Contains("GreaterThan", score.Operators);
        Assert.Contains("LessThanOrEqual", score.Operators);

        FilterFieldDescriptor tags = Field(descriptor, nameof(DescribedEvent.Tags));
        Assert.Equal(FilterFieldKind.Array, tags.Kind);
        Assert.Contains("Contains", tags.Operators);
        Assert.Contains("Count", tags.Operators);
    }

    [Fact]
    public void DescriptorIsJsonSerializable()
    {
        FilterSchemaDescriptor descriptor = FilterSchema.For(typeof(DescribedEvent)).Describe();

        string json = JsonSerializer.Serialize(descriptor);
        FilterSchemaDescriptor? restored = JsonSerializer.Deserialize<FilterSchemaDescriptor>(json);

        Assert.NotNull(restored);
        Assert.Equal(descriptor.Fields.Count, restored!.Fields.Count);
    }

    private static FilterFieldDescriptor Field(FilterSchemaDescriptor descriptor, string name) =>
        descriptor.Fields.Single(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));

    private sealed record DescribedEvent(string Name, int Score, string[] Tags) : IFilterSubject;
}

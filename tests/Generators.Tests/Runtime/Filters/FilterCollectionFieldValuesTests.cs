using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterCollectionFieldValuesTests
{
    [Theory]
    [InlineData(".Items")]
    [InlineData("Items..Name")]
    [InlineData("Items. .Name")]
    public void ReadRejectsMalformedPropertyPath(string propertyPath)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            FilterCollectionFieldValues.Read(new CollectionSubject([]), propertyPath));

        Assert.Contains(propertyPath, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadIncludesPropertyPathWhenFlattenedValuesExceedLimit()
    {
        CollectionItem[] items = Enumerable.Range(0, 257)
            .Select(static index => new CollectionItem(index.ToString()))
            .ToArray();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FilterCollectionFieldValues.Read(new CollectionSubject(items), "Items.Name"));

        Assert.Contains("Items.Name", ex.Message, StringComparison.Ordinal);
    }

    private sealed record CollectionSubject(CollectionItem[] Items);

    private sealed record CollectionItem(string Name);
}

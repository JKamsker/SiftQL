using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class CollectionTraversalLimitRegressionTests
{
    [Fact]
    public void ReadLimitsTraversedItemsEvenWhenNoValuesAreProduced()
    {
        CollectionItem[] items = Enumerable.Range(0, 257)
            .Select(static _ => new CollectionItem(null))
            .ToArray();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            FilterCollectionFieldValues.Read(new CollectionSubject(items), "Items.Name.Value"));

        Assert.Contains("Items.Name.Value", ex.Message, StringComparison.Ordinal);
    }

    private sealed record CollectionSubject(CollectionItem[] Items);

    private sealed record CollectionItem(CollectionName? Name);

    private sealed record CollectionName(string Value);
}

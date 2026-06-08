using SiftQL.Hot;

namespace SiftQL.Generators.Tests;

public sealed class HotManifestSemanticHashRegressionTests
{
    [Fact]
    public void SemanticHashIgnoresDuplicateEquivalentEntries()
    {
        string entry = Entry("""
            {
                "Kind": 4,
                "Field": "ItemId",
                "Operator": 0,
                "Value": { "Kind": 2, "Integer": 100 },
                "Values": [],
                "Children": []
            }
            """);

        string single = Manifest(entry);
        string duplicate = Manifest(entry + "," + entry);

        Assert.Equal(
            HotManifestSemanticHash.Compute(single),
            HotManifestSemanticHash.Compute(duplicate));
    }

    [Fact]
    public void SemanticHashCanonicalizesEquivalentDecimalNumbers()
    {
        string onePointOne = Manifest(Entry("""
            {
                "Kind": 4,
                "Field": "Amount",
                "Operator": 0,
                "Value": { "Kind": 7, "Decimal": 1.1 },
                "Values": [],
                "Children": []
            }
            """));
        string onePointTen = Manifest(Entry("""
            {
                "Kind": 4,
                "Field": "Amount",
                "Operator": 0,
                "Value": { "Kind": 7, "Decimal": 1.10 },
                "Values": [],
                "Children": []
            }
            """));

        Assert.Equal(
            HotManifestSemanticHash.Compute(onePointOne),
            HotManifestSemanticHash.Compute(onePointTen));
    }

    private static string Entry(string definition) =>
        $$"""
        {
            "Key": "filter|Subject|fingerprint",
            "Kind": "filter",
            "SubjectType": "Subject",
            "Fingerprint": "fingerprint",
            "Definition": {{definition}}
        }
        """;

    private static string Manifest(string entries) =>
        $$"""
        {
            "Schema": "siftql.hot.v1",
            "RuntimeVersion": "10.0.0",
            "FilterEngineVersion": "tiered-v1",
            "GeneratorVersion": "hot-sourcegen-v1",
            "Entries": [{{entries}}]
        }
        """;
}

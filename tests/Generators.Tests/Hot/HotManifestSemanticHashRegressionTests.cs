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

    [Fact]
    public void SemanticHashDistinguishesProjectionArgumentNegativeZeroDefinition()
    {
        string positiveZero = Manifest(ProjectionEntry("""
            {
                "Fields": [],
                "Includes": [
                    {
                        "Intrinsic": "test.window",
                        "ResultName": "window",
                        "Arguments": [
                            {
                                "Name": "offset",
                                "Value": { "Kind": 3, "Number": 0.0 }
                            }
                        ]
                    }
                ]
            }
            """));
        string negativeZero = Manifest(ProjectionEntry("""
            {
                "Fields": [],
                "Includes": [
                    {
                        "Intrinsic": "test.window",
                        "ResultName": "window",
                        "Arguments": [
                            {
                                "Name": "offset",
                                "Value": { "Kind": 3, "Number": -0.0 }
                            }
                        ]
                    }
                ]
            }
            """));

        Assert.NotEqual(
            HotManifestSemanticHash.Compute(positiveZero),
            HotManifestSemanticHash.Compute(negativeZero));
    }

    private static string Entry(string definition) =>
        Entry("filter", definition);

    private static string ProjectionEntry(string definition) =>
        Entry("projection", definition);

    private static string Entry(string kind, string definition) =>
        $$"""
        {
            "Key": "{{kind}}|Subject|fingerprint",
            "Kind": "{{kind}}",
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

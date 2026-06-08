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
    public void SemanticHashIgnoresRuntimeVersion()
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

        Assert.Equal(
            HotManifestSemanticHash.Compute(Manifest(entry, runtimeVersion: "10.0.0")),
            HotManifestSemanticHash.Compute(Manifest(entry, runtimeVersion: "11.0.0")));
    }

    [Fact]
    public void SemanticHashIgnoresParameterizedFilterLiteralValues()
    {
        string seven = Manifest(Entry("""
            {
                "Kind": 4,
                "Field": "ItemId",
                "Operator": 0,
                "Value": { "Kind": 2, "ParameterKey": "p0", "Integer": 7 },
                "Values": [],
                "Children": []
            }
            """));
        string nine = Manifest(Entry("""
            {
                "Kind": 4,
                "Field": "ItemId",
                "Operator": 0,
                "Value": { "Kind": 2, "ParameterKey": "p0", "Integer": 9 },
                "Values": [],
                "Children": []
            }
            """));

        Assert.Equal(
            HotManifestSemanticHash.Compute(seven),
            HotManifestSemanticHash.Compute(nine));
    }

    [Fact]
    public void SemanticHashIgnoresParameterizedProjectionArgumentLiteralValues()
    {
        string three = Manifest(ProjectionEntry("""
            {
                "Fields": [],
                "Includes": [
                    {
                        "Intrinsic": "test.limit",
                        "ResultName": "limit",
                        "Arguments": [
                            {
                                "Name": "limit",
                                "Value": { "Kind": 2, "ParameterKey": "p0", "Integer": 3 }
                            }
                        ]
                    }
                ]
            }
            """));
        string five = Manifest(ProjectionEntry("""
            {
                "Fields": [],
                "Includes": [
                    {
                        "Intrinsic": "test.limit",
                        "ResultName": "limit",
                        "Arguments": [
                            {
                                "Name": "limit",
                                "Value": { "Kind": 2, "ParameterKey": "p0", "Integer": 5 }
                            }
                        ]
                    }
                ]
            }
            """));

        Assert.Equal(
            HotManifestSemanticHash.Compute(three),
            HotManifestSemanticHash.Compute(five));
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

    private static string Manifest(string entries, string runtimeVersion = "10.0.0") =>
        $$"""
        {
            "Schema": "siftql.hot.v1",
            "RuntimeVersion": "{{runtimeVersion}}",
            "FilterEngineVersion": "tiered-v1",
            "GeneratorVersion": "hot-sourcegen-v1",
            "Entries": [{{entries}}]
        }
        """;
}

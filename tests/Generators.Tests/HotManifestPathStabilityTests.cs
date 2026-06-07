using System.Reflection;
using System.Text;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SiftQL.Generators.Tests;

internal static class HotManifestPathStabilityTests
{
    public static void RunAll() => ProviderNameIncludesDirectoryIdentity();

    private static void ProviderNameIncludesDirectoryIdentity()
    {
        string manifest = """
            {
              "Schema": "siftql.hot.v1",
              "RuntimeVersion": "10.0.0",
              "FilterEngineVersion": "tiered-v1",
              "GeneratorVersion": "hot-sourcegen-v1",
              "Entries": []
            }
            """;

        object first = Parse(@"C:\agent-a\filters.siftql-hot.json", manifest);
        object second = Parse(@"D:\agent-b\filters.siftql-hot.json", manifest);

        AssertEx.NotEqual(Property(first, "ProviderName"), Property(second, "ProviderName"), "provider name should include path identity");
        AssertEx.NotEqual(Property(first, "HintName"), Property(second, "HintName"), "hint name should include path identity");
    }

    private static object Parse(string path, string manifest)
    {
        Type parser = typeof(FilterSchemaSourceGenerator).Assembly.GetType(
            "SiftQL.Generators.Hot.HotManifestParser",
            throwOnError: true)!;
        return parser.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [new InMemoryAdditionalText(path, manifest), CancellationToken.None])!;
    }

    private static string Property(object result, string name) =>
        (string)result.GetType().GetProperty(name)!.GetValue(result)!;

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }
}

using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Compiler;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projection;
using SiftQL.Values;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

internal static class GeneratorTestCompilation
{
    // MetadataReference.CreateFromFile parses each assembly's metadata, and
    // the trusted platform list spans the whole shared framework. References
    // are immutable and shareable, so build the list once for all tests.
    private static readonly Lazy<MetadataReference[]> s_references = new(BuildReferences);

    public static CSharpCompilation Create(string assemblyName, params SyntaxTree[] syntaxTrees) =>
        CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: syntaxTrees,
            references: s_references.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static MetadataReference[] BuildReferences()
    {
        List<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        AddReference(references, typeof(FilterExpression).Assembly.Location);
        AddReference(references, typeof(FilterCompiler).Assembly.Location);
        return [.. references];
    }

    private static void AddReference(List<MetadataReference> references, string path)
    {
        if (!references.OfType<PortableExecutableReference>().Any(item => item.FilePath == path))
            references.Add(MetadataReference.CreateFromFile(path));
    }
}

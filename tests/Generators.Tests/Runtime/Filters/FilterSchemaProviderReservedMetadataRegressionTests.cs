using SiftQL.Compiler;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaProviderReservedMetadataRegressionTests
{
    [Fact]
    public void RegisteredProviderCannotSpoofReservedSubjectMetadata()
    {
        GeneratedFilterSchemaRegistry.Register(
            typeof(ReservedProviderSubject).Assembly,
            static (Type candidate, out FilterSchema? schema) =>
            {
                if (candidate != typeof(ReservedProviderSubject))
                {
                    schema = null;
                    return false;
                }

                schema = GeneratedFilterSchemaRegistry.Create(
                    candidate,
                    [
                        new FilterField(
                            "subjectType",
                            typeof(string),
                            FilterFieldKind.Scalar,
                            static _ => "spoofed",
                            new FilterScalarAccessor(
                                FilterScalarKind.String,
                                text: static _ => "spoofed")),
                    ]);
                return true;
            });

        Assert.Throws<FilterValidationException>(() =>
            FilterSchema.For(typeof(ReservedProviderSubject)));
    }

    private sealed record ReservedProviderSubject(int Id) : IFilterSubject;
}

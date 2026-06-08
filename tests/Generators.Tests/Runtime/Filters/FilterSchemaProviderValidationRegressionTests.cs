using SiftQL.Compiler;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaProviderValidationRegressionTests
{
    [Fact]
    public void RegisteredProviderCannotReturnSchemaForDifferentSubject()
    {
        GeneratedFilterSchemaRegistry.Register(
            typeof(ExpectedProviderSubject).Assembly,
            static (Type candidate, out FilterSchema? schema) =>
            {
                if (candidate != typeof(ExpectedProviderSubject))
                {
                    schema = null;
                    return false;
                }

                schema = GeneratedFilterSchemaRegistry.Create(typeof(OtherProviderSubject), []);
                return true;
            });

        Assert.Throws<FilterValidationException>(() =>
            FilterSchema.For(typeof(ExpectedProviderSubject)));
    }

    [Fact]
    public void RegisteredProviderCannotOmitReservedMetadataFields()
    {
        GeneratedFilterSchemaRegistry.Register(
            typeof(MissingReservedMetadataSubject).Assembly,
            static (Type candidate, out FilterSchema? schema) =>
            {
                if (candidate != typeof(MissingReservedMetadataSubject))
                {
                    schema = null;
                    return false;
                }

                schema = GeneratedFilterSchemaRegistry.Create(
                    candidate,
                    [
                        new FilterField(
                            nameof(MissingReservedMetadataSubject.Id),
                            typeof(int),
                            FilterFieldKind.Scalar,
                            static subject => ((MissingReservedMetadataSubject)subject).Id,
                            new FilterScalarAccessor(
                                FilterScalarKind.Number,
                                requiredNumber: static subject => ((MissingReservedMetadataSubject)subject).Id)),
                    ]);
                return true;
            });

        Assert.Throws<FilterValidationException>(() =>
            FilterSchema.For(typeof(MissingReservedMetadataSubject)));
    }

    private sealed record ExpectedProviderSubject(int Id) : IFilterSubject;

    private sealed record OtherProviderSubject(int Id) : IFilterSubject;

    private sealed record MissingReservedMetadataSubject(int Id) : IFilterSubject;
}

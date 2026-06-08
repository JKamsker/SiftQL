using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
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

    [Fact]
    public void RegisteredProviderCannotUseConstantAccessForUnsealedReservedMetadata()
    {
        GeneratedFilterSchemaRegistry.Register(
            typeof(UnsealedReservedProviderSubject).Assembly,
            static (Type candidate, out FilterSchema? schema) =>
            {
                if (candidate != typeof(UnsealedReservedProviderSubject))
                {
                    schema = null;
                    return false;
                }

                schema = GeneratedFilterSchemaRegistry.Create(
                    candidate,
                    [ReservedSubjectName(access: FilterFieldAccess.ForConstant(nameof(UnsealedReservedProviderSubject)))]);
                return true;
            });

        Assert.Throws<FilterValidationException>(() =>
            FilterSchema.For(typeof(UnsealedReservedProviderSubject)));
    }

    [Fact]
    public void RegisteredProviderCannotUseConstantAccessForInterfaceReservedMetadata()
    {
        GeneratedFilterSchemaRegistry.Register(
            typeof(InterfaceReservedProviderSubject).Assembly,
            static (Type candidate, out FilterSchema? schema) =>
            {
                if (candidate != typeof(InterfaceReservedProviderSubject))
                {
                    schema = null;
                    return false;
                }

                schema = GeneratedFilterSchemaRegistry.Create(
                    candidate,
                    [ReservedSubjectName(access: FilterFieldAccess.ForConstant(nameof(InterfaceReservedProviderSubject)))]);
                return true;
            });

        Assert.Throws<FilterValidationException>(() =>
            FilterSchema.For(typeof(InterfaceReservedProviderSubject)));
    }

    [Fact]
    public void RegisteredProviderCannotUseStaleProjectionAccessorForReservedMetadata()
    {
        GeneratedFilterSchemaRegistry.Register(
            typeof(UnsealedReservedProviderSubject).Assembly,
            static (Type candidate, out FilterSchema? schema) =>
            {
                if (candidate != typeof(UnsealedReservedProviderSubject))
                {
                    schema = null;
                    return false;
                }

                schema = GeneratedFilterSchemaRegistry.Create(
                    candidate,
                    [
                        ReservedSubjectName(
                            projectionAccessor: static _ =>
                                ProjectedEventValue.FromScalar(nameof(UnsealedReservedProviderSubject))),
                    ]);
                return true;
            });

        Assert.Throws<FilterValidationException>(() =>
            FilterSchema.For(typeof(UnsealedReservedProviderSubject)));
    }

    private static FilterField ReservedSubjectName(
        FilterFieldAccess? access = null,
        Func<object, ProjectedEventValue>? projectionAccessor = null) =>
        new(
            "subjectName",
            typeof(string),
            FilterFieldKind.Scalar,
            static subject => subject.GetType().Name,
            new FilterScalarAccessor(
                FilterScalarKind.String,
                text: static subject => subject.GetType().Name),
            ProjectionAccessor: projectionAccessor,
            Access: access);

    private sealed record ReservedProviderSubject(int Id) : IFilterSubject;

    private record UnsealedReservedProviderSubject(int Id) : IFilterSubject;

    private interface InterfaceReservedProviderSubject : IFilterSubject
    {
        int Id { get; }
    }
}

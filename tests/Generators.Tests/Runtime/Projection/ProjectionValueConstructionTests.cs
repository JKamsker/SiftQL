using System.Linq.Expressions;
using SiftQL.Projected;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionValueConstructionTests
{
    [Theory]
    [MemberData(nameof(FactoryCases))]
    public void ProjectionValueFactoryMapsScalarValues(
        ProjectedEventValue value,
        ProjectedEventValueKind expectedKind,
        object? expected)
    {
        Assert.Equal(expectedKind, value.Kind);
        AssertProjectedValue(value, expected);
    }

    [Fact]
    public void ProjectionValueExpressionBuildsNullableAndEnumAccessors()
    {
        ParameterExpression parameter = Expression.Parameter(typeof(object), "subject");
        Expression typed = Expression.Convert(parameter, typeof(ProjectionSubject));

        Func<object, ProjectedEventValue> optionalCount = CompileAccessor(
            typeof(int),
            Expression.Property(typed, nameof(ProjectionSubject.OptionalCount)),
            parameter);
        Func<object, ProjectedEventValue> kind = CompileAccessor(
            typeof(ProjectionKind),
            Expression.Property(typed, nameof(ProjectionSubject.Kind)),
            parameter);

        var present = new ProjectionSubject(7, ProjectionKind.Target);
        var missing = new ProjectionSubject(null, ProjectionKind.Other);

        Assert.Equal(7, optionalCount(present).Integer);
        Assert.Equal(ProjectedEventValueKind.Null, optionalCount(missing).Kind);
        Assert.Equal("Target", kind(present).String);
    }

    [Fact]
    public void ProjectionValueExpressionReturnsNullForUnsupportedTypes()
    {
        ParameterExpression parameter = Expression.Parameter(typeof(object), "subject");
        Expression value = Expression.Constant(DateTimeOffset.UnixEpoch);

        Assert.Null(ProjectionValueExpression.CompileAccessor(
            typeof(DateTimeOffset),
            value,
            parameter));
    }

    public static TheoryData<ProjectedEventValue, ProjectedEventValueKind, object?> FactoryCases() =>
        new()
        {
            { ProjectionValueFactory.FromBoolean(true), ProjectedEventValueKind.Boolean, true },
            { ProjectionValueFactory.FromBoolean((bool?)null), ProjectedEventValueKind.Null, null },
            { ProjectionValueFactory.FromInt64(-7), ProjectedEventValueKind.Integer, -7L },
            { ProjectionValueFactory.FromUInt64(ulong.MaxValue), ProjectedEventValueKind.UnsignedInteger, ulong.MaxValue },
            { ProjectionValueFactory.FromDouble(1.5D), ProjectedEventValueKind.Number, 1.5D },
            { ProjectionValueFactory.FromDecimal(1.25m), ProjectedEventValueKind.Decimal, 1.25m },
            { ProjectionValueFactory.FromString(string.Empty), ProjectedEventValueKind.String, string.Empty },
            { ProjectionValueFactory.FromString(null), ProjectedEventValueKind.Null, null },
            { ProjectionValueFactory.FromGuid(Guid.Empty), ProjectedEventValueKind.Guid, Guid.Empty },
            { ProjectionValueFactory.FromEnum(ProjectionKind.Target), ProjectedEventValueKind.String, "Target" },
        };

    private static Func<object, ProjectedEventValue> CompileAccessor(
        Type valueType,
        Expression value,
        ParameterExpression parameter) =>
        ProjectionValueExpression.CompileAccessor(valueType, value, parameter) ??
            throw new InvalidOperationException("Expected projection accessor.");

    private static void AssertProjectedValue(ProjectedEventValue value, object? expected)
    {
        switch (expected)
        {
            case null:
                Assert.Equal(ProjectedEventValueKind.Null, value.Kind);
                break;
            case bool item:
                Assert.Equal(item, value.Boolean);
                break;
            case long item:
                Assert.Equal(item, value.Integer);
                break;
            case ulong item:
                Assert.Equal(item, value.UnsignedInteger);
                break;
            case double item:
                Assert.Equal(item, value.Number);
                break;
            case decimal item:
                Assert.Equal(item, value.Decimal);
                break;
            case string item:
                Assert.Equal(item, value.String);
                break;
            case Guid item:
                Assert.Equal(item, value.Guid);
                break;
        }
    }

    private sealed record ProjectionSubject(int? OptionalCount, ProjectionKind Kind);

    private enum ProjectionKind
    {
        Other,
        Target,
    }
}

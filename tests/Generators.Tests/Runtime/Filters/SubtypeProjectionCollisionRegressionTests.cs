using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class SubtypeProjectionCollisionRegressionTests
{
    [Fact]
    public void SameNamedSubtypesUseDistinctProjectedFieldPaths()
    {
        FilterExpression first = QueryKernel.For<CollisionCombat>()
            .Where(static combat => (combat.Actor as FactionA.Unit)!.Level > 5)
            .Filter;
        FilterExpression second = QueryKernel.For<CollisionCombat>()
            .Where(static combat => (combat.Actor as FactionB.Unit)!.Level > 5)
            .Filter;

        Assert.NotEqual(first.Field, second.Field);
    }

    private abstract record CollisionEntity;
    private sealed record CollisionCombat(CollisionEntity? Actor) : IFilterSubject;

    private static class FactionA
    {
        public sealed record Unit(int Level = 0) : CollisionEntity;
    }

    private static class FactionB
    {
        public sealed record Unit(int Level = 0) : CollisionEntity;
    }
}

using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

// Covers the C# `is` operator (ExpressionType.TypeIs) in filter predicates and
// guards that `as` casts stay transparent. `x is T` lowers to a Contains over
// the synthetic `subjectTypes` ancestry discriminator, so it matches T and every
// subtype/interface implementation, mirroring the CLR `is` operator.
public sealed class KernelTypeTestTranslationTests
{
    private interface IHasHealth
    {
        int Hp { get; }
    }

    private abstract record Entity(string Tag = "") : IFilterSubject;

    private sealed record Player(string Tag = "", int Level = 0) : Entity(Tag);

    private record Monster(string Tag = "", int Hp = 0) : Entity(Tag), IHasHealth;

    private sealed record Orc(string Tag = "", int Hp = 0, int Rage = 0) : Monster(Tag, Hp);

    private sealed record Combat(
        Entity? Attacker = null,
        Entity? Defender = null,
        bool IsLongAttack = false,
        int Damage = 0) : IFilterSubject;

    // ---- Translation shape --------------------------------------------------

    [Fact]
    public void RootTypeTestTranslatesToSubjectTypesContains()
    {
        FilterExpression filter = QueryKernel.For<Entity>()
            .Where(static e => e is Player)
            .Filter;

        Assert.Equal(FilterExpressionKind.Contains, filter.Kind);
        Assert.Equal("subjectTypes", filter.Field);
        Assert.Equal(typeof(Player).FullName, filter.Value!.String);
    }

    [Fact]
    public void MemberTypeTestTranslatesToNestedDiscriminator()
    {
        FilterExpression filter = QueryKernel.For<Combat>()
            .Where(static c => c.Defender is Monster)
            .Filter;

        Assert.Equal(FilterExpressionKind.Contains, filter.Kind);
        Assert.Equal("Defender.subjectTypes", filter.Field);
        Assert.Equal(typeof(Monster).FullName, filter.Value!.String);
    }

    // ---- Runtime matching: root subject -------------------------------------

    [Fact]
    public void RootTypeTestMatchesExactType()
    {
        CompiledKernel kernel = Compile<Entity>(static e => e is Player);

        Assert.True(kernel.Matches(new Player()));
        Assert.False(kernel.Matches(new Monster()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RootTypeTestMatchesSubtypes(bool tiered)
    {
        CompiledKernel kernel = Compile<Entity>(static e => e is Monster, tiered);

        // Orc : Monster — the CLR `is` matches subtypes, and so must the filter.
        Assert.True(kernel.Matches(new Monster()));
        Assert.True(kernel.Matches(new Orc()));
        Assert.False(kernel.Matches(new Player()));
    }

    [Fact]
    public void RootTypeTestMatchesInterfaceImplementations()
    {
        CompiledKernel kernel = Compile<Entity>(static e => e is IHasHealth);

        Assert.True(kernel.Matches(new Monster()));
        Assert.True(kernel.Matches(new Orc()));
        Assert.False(kernel.Matches(new Player()));
    }

    [Fact]
    public void NegatedTypeTestInvertsMatch()
    {
        CompiledKernel kernel = Compile<Entity>(static e => !(e is Player));

        Assert.False(kernel.Matches(new Player()));
        Assert.True(kernel.Matches(new Monster()));
    }

    // ---- Runtime matching: nested member (the example query) ----------------

    [Fact]
    public void MemberTypeTestMatchesDefenderSubtype()
    {
        FilterSchema.RegisterValueObject(typeof(Entity));
        CompiledKernel kernel = Compile<Combat>(static c => c.Defender is Monster);

        Assert.True(kernel.Matches(new Combat(Defender: new Monster())));
        Assert.True(kernel.Matches(new Combat(Defender: new Orc())));
        Assert.False(kernel.Matches(new Combat(Defender: new Player())));
        Assert.False(kernel.Matches(new Combat(Defender: null)));
    }

    [Fact]
    public void CombinedTypeTestAndComparisonMatches()
    {
        FilterSchema.RegisterValueObject(typeof(Entity));
        CompiledKernel kernel = Compile<Combat>(
            static c => c.Defender is Monster && c.Damage > 0);

        Assert.True(kernel.Matches(new Combat(Defender: new Orc(), Damage: 5)));
        Assert.False(kernel.Matches(new Combat(Defender: new Orc(), Damage: 0)));
        Assert.False(kernel.Matches(new Combat(Defender: new Player(), Damage: 5)));
    }

    // ---- `as` cast stays transparent ---------------------------------------

    [Fact]
    public void AsCastToDerivedFieldStaysTransparentInTranslation()
    {
        // (attacker as Player).Level lowers to the field path "Attacker.Level":
        // the cast is stripped, leaving an ordinary scalar comparison.
        FilterExpression filter = QueryKernel.For<Combat>()
            .Where(static c => (c.Attacker as Player)!.Level > 5)
            .Filter;

        Assert.Equal(FilterExpressionKind.Compare, filter.Kind);
        Assert.Equal("Attacker.Level", filter.Field);
        Assert.Equal(FilterOperator.GreaterThan, filter.Operator);
    }

    [Fact]
    public void AsCastMemberAccessMatchesAtRuntime()
    {
        FilterSchema.RegisterValueObject(typeof(Entity));
        // Tag is declared on the base Entity, so the schema resolves it through
        // the cast and the predicate evaluates end-to-end.
        CompiledKernel kernel = Compile<Combat>(
            static c => (c.Attacker as Player)!.Tag == "boss");

        Assert.True(kernel.Matches(new Combat(Attacker: new Player(Tag: "boss"))));
        Assert.False(kernel.Matches(new Combat(Attacker: new Player(Tag: "grunt"))));
    }

    [Fact]
    public void ExampleQueryCombiningAsAndIsCompilesAndMatches()
    {
        FilterSchema.RegisterValueObject(typeof(Entity));
        // Mirrors the shape of the motivating query: a boolean flag, an `as`
        // cast member access, a numeric comparison, and an `is` type test.
        CompiledKernel kernel = Compile<Combat>(static c =>
            c.IsLongAttack &&
            (c.Attacker as Player)!.Tag == "boss" &&
            c.Damage > 0 &&
            c.Defender is Monster);

        Assert.True(kernel.Matches(new Combat(
            Attacker: new Player(Tag: "boss"),
            Defender: new Orc(),
            IsLongAttack: true,
            Damage: 12)));

        // Defender is a Player, not a Monster -> filtered out.
        Assert.False(kernel.Matches(new Combat(
            Attacker: new Player(Tag: "boss"),
            Defender: new Player(),
            IsLongAttack: true,
            Damage: 12)));

        // Not a long attack -> filtered out.
        Assert.False(kernel.Matches(new Combat(
            Attacker: new Player(Tag: "boss"),
            Defender: new Orc(),
            IsLongAttack: false,
            Damage: 12)));
    }

    // ---- Two-parameter (context) predicates --------------------------------

    [Fact]
    public async Task ContextPredicateMemberTypeTestFiltersBySubtype()
    {
        FilterSchema.RegisterValueObject(typeof(Entity));
        var query = QueryKernel.For<Combat, GameContext>()
            .Where(static (c, ctx) => c.Defender is Monster);
        CompiledEventPipeline<GameContext> compiled = EventPipelineCompiler.Compile<GameContext>(
            typeof(Combat),
            query.Pipeline,
            EventPipelineCompilerOptions.Immediate);

        ProjectedEvent? matched = await compiled.ProjectAsync(
            new Combat(Defender: new Orc()),
            new GameContext(),
            CancellationToken.None);
        ProjectedEvent? missed = await compiled.ProjectAsync(
            new Combat(Defender: new Player()),
            new GameContext(),
            CancellationToken.None);

        Assert.NotNull(matched);
        Assert.Null(missed);
    }

    private sealed class GameContext;

    private static CompiledKernel Compile<TSubject>(
        System.Linq.Expressions.Expression<Func<TSubject, bool>> predicate,
        bool tiered = false)
        where TSubject : IFilterSubject
    {
        FilterExpression filter = QueryKernel.For<TSubject>().Where(predicate).Filter;
        return FilterCompiler.Compile(
            typeof(TSubject),
            filter,
            tiered ? FilterCompilerOptions.Tiered : FilterCompilerOptions.Immediate);
    }
}

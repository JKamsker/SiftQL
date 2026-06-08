using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class QueryContextDescriptorRegressionTests
{
    [Fact]
    public void ContextIntrinsicsParseQualifiedAndLegacyMethods()
    {
        string qualified = EventProjectionContextIntrinsics.Method(
            "sample.users",
            "user",
            "Profile.Tier");

        Assert.True(EventProjectionContextIntrinsics.TryParseMethod(
            qualified,
            out string contextId,
            out string methodId,
            out string memberPath));
        Assert.Equal("sample.users", contextId);
        Assert.Equal("user", methodId);
        Assert.Equal("Profile.Tier", memberPath);

        string legacy = EventProjectionContextIntrinsics.Method("User", "Tier");
        Assert.False(EventProjectionContextIntrinsics.TryParseMethod(legacy, out _, out _, out _));
        Assert.True(EventProjectionContextIntrinsics.TryParseLegacyMethod(
            legacy,
            out string methodName,
            out string legacyMemberPath));
        Assert.Equal("User", methodName);
        Assert.Equal("Tier", legacyMemberPath);
    }

    [Fact]
    public void RegisteredContextDescriptorsProduceQualifiedIncludes()
    {
        SiftQueryContextRegistry.Register(new SiftQueryContextDescriptor(
            typeof(IUserQueryContext),
            "sample.users",
            [
                new SiftQueryContextMethodDescriptor(
                    nameof(IUserQueryContext.User),
                    "user",
                    typeof(UserSnapshot),
                    [
                        new SiftQueryContextParameterDescriptor(
                            "userId",
                            typeof(long),
                            false,
                            null),
                    ]),
            ]));

        QueryKernel<IUserEvent> query = QueryKernel
            .For<IUserEvent>()
            .WithContext<IUserEvent, IUserQueryContext>()
            .Where(static (ev, ctx) => ctx.User(ev.UserId).IsActive)
            .Select(static (ev, ctx) => new
            {
                Active = ctx.User(ev.UserId).IsActive,
            })
            .ToQueryKernel();

        EventProjectionInclude include = query.Pipeline.Stages
            .Where(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .SelectMany(static stage => stage.Projection.Includes)
            .Single();
        Assert.True(EventProjectionContextIntrinsics.TryParseMethod(
            include.Intrinsic,
            out string contextId,
            out string methodId,
            out string memberPath));
        Assert.Equal("sample.users", contextId);
        Assert.Equal("user", methodId);
        Assert.Equal(nameof(UserSnapshot.IsActive), memberPath);
    }

    [Fact]
    public void UnregisteredContextDescriptorsKeepLegacyIncludes()
    {
        QueryKernel<IUserEvent> query = QueryKernel
            .For<IUserEvent>()
            .WithContext<IUserEvent, LegacyUserContext>()
            .Where(static (ev, ctx) => ctx.User(ev.UserId).IsActive)
            .ToQueryKernel();

        EventProjectionInclude include = query.Pipeline.Stages
            .Where(static stage => stage.Kind == EventPipelineStageKind.Projection)
            .SelectMany(static stage => stage.Projection.Includes)
            .Single();

        Assert.False(EventProjectionContextIntrinsics.TryParseMethod(include.Intrinsic, out _, out _, out _));
        Assert.True(EventProjectionContextIntrinsics.TryParseLegacyMethod(
            include.Intrinsic,
            out string methodName,
            out string memberPath));
        Assert.Equal(nameof(LegacyUserContext.User), methodName);
        Assert.Equal(nameof(UserSnapshot.IsActive), memberPath);
    }

    private interface IUserQueryContext
    {
        UserSnapshot User(long userId);
    }

    private sealed class LegacyUserContext
    {
        public UserSnapshot User(long userId) =>
            new(userId > 0);
    }

    private sealed record UserSnapshot(bool IsActive);

    private sealed record IUserEvent(long UserId) : IFilterSubject;
}

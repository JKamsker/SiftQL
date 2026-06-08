using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Index;
using SiftQL.Kernel;

namespace SiftQL.Generators.Tests;

public sealed class FilterEnumCompatibilityRegressionTests
{
    [Fact]
    public void FlagsEnumStringCompareUsesParseSemantics()
    {
        var filter = FilterExpression.Compare(
            nameof(FlagSubject.Permissions),
            FilterOperator.Equal,
            FilterValue.From("Write, Read"));

        AssertFilter(filter, new FilterCase<FlagSubject>(
            new FlagSubject(Permissions.Read | Permissions.Write),
            true));
    }

    [Fact]
    public void FlagsEnumStringInUsesParseSemantics()
    {
        var filter = FilterExpression.In(
            nameof(FlagSubject.Permissions),
            [FilterValue.From("Write, Read")]);

        AssertFilter(filter, new FilterCase<FlagSubject>(
            new FlagSubject(Permissions.Read | Permissions.Write),
            true));
    }

    [Fact]
    public void NullableFlagsEnumArrayContainsUsesParseSemantics()
    {
        var filter = FilterExpression.Contains(
            nameof(NullableFlagArraySubject.Permissions),
            FilterValue.From("Write, Read"));

        AssertFilter(filter, new FilterCase<NullableFlagArraySubject>(
            new NullableFlagArraySubject([Permissions.Read | Permissions.Write]),
            true));
    }

    [Fact]
    public void FlagsEnumStringIndexMatchesUseSameSemanticsAsCandidates()
    {
        var filter = FilterExpression.Compare(
            nameof(FlagSubject.Permissions),
            FilterOperator.Equal,
            FilterValue.From("Write, Read"));
        var index = new FilterSubscriptionIndex<string>(typeof(FlagSubject));
        index.Add("flags", filter);
        var subject = new FlagSubject(Permissions.Read | Permissions.Write);

        Assert.Equal(["flags"], index.SnapshotCandidates(subject));
        Assert.Equal(["flags"], index.SnapshotMatches(subject));
    }

    private static void AssertFilter<TSubject>(
        FilterExpression filter,
        params FilterCase<TSubject>[] cases)
    {
        CompiledKernel immediate = FilterCompiler.Compile(
            typeof(TSubject),
            filter,
            FilterCompilerOptions.Immediate);
        CompiledKernel tiered = FilterCompiler.Compile(
            typeof(TSubject),
            filter,
            FilterCompilerOptions.Tiered);

        foreach (FilterCase<TSubject> item in cases)
        {
            Assert.Equal(item.Expected, immediate.Matches(item.Subject!));
            Assert.Equal(item.Expected, tiered.Matches(item.Subject!));
        }
    }

    [Flags]
    private enum Permissions
    {
        None = 0,
        Read = 1,
        Write = 2,
    }

    private sealed record FlagSubject(Permissions Permissions) : IFilterSubject;

    private sealed record NullableFlagArraySubject(Permissions?[] Permissions) : IFilterSubject;

    private sealed record FilterCase<TSubject>(TSubject Subject, bool Expected);
}

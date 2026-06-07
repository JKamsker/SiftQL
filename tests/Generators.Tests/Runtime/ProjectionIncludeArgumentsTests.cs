using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projection;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionIncludeArgumentsTests
{
    private static EventProjectionInclude MakeInclude(string intrinsic, params EventProjectionArgument[] args) =>
        new(intrinsic, "result", args);

    [Fact]
    public void IncludeArgs_RequiredString_ReturnsValue()
    {
        var include = MakeInclude("op", new EventProjectionArgument("name", FilterValue.From("hello")));
        Assert.Equal("hello", ProjectionIncludeArguments.RequiredString(include, "name"));
    }

    [Fact]
    public void IncludeArgs_RequiredString_CaseInsensitive()
    {
        var include = MakeInclude("op", new EventProjectionArgument("Name", FilterValue.From("world")));
        Assert.Equal("world", ProjectionIncludeArguments.RequiredString(include, "name"));
    }

    [Fact]
    public void IncludeArgs_RequiredString_MissingArg_Throws()
        => Assert.Throws<FilterValidationException>(() =>
            ProjectionIncludeArguments.RequiredString(MakeInclude("op"), "missing"));

    [Fact]
    public void IncludeArgs_RequiredString_WrongType_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("num", FilterValue.From(42L)));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredString(include, "num"));
    }

    [Fact]
    public void IncludeArgs_RequiredString_WhitespaceOnly_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("ws", FilterValue.From("   ")));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredString(include, "ws"));
    }

    [Fact]
    public void IncludeArgs_RequiredString_CustomErrorFactory_UsedOnMissing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProjectionIncludeArguments.RequiredString(MakeInclude("op"), "x", msg => new InvalidOperationException(msg)));
        Assert.Contains("missing argument", ex.Message);
    }

    [Fact]
    public void IncludeArgs_RequiredString_CustomErrorFactory_UsedOnWrongType()
    {
        var include = MakeInclude("op", new EventProjectionArgument("n", FilterValue.From(1L)));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProjectionIncludeArguments.RequiredString(include, "n", msg => new InvalidOperationException(msg)));
        Assert.Contains("must be a string", ex.Message);
    }

    [Fact]
    public void IncludeArgs_RequiredInt_ReturnsValue()
    {
        var include = MakeInclude("op", new EventProjectionArgument("count", FilterValue.From(7L)));
        Assert.Equal(7, ProjectionIncludeArguments.RequiredInt(include, "count"));
    }

    [Fact]
    public void IncludeArgs_RequiredInt_MissingArg_Throws()
        => Assert.Throws<FilterValidationException>(() =>
            ProjectionIncludeArguments.RequiredInt(MakeInclude("op"), "n"));

    [Fact]
    public void IncludeArgs_RequiredInt_WrongKind_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("val", FilterValue.From("not-int")));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredInt(include, "val"));
    }

    [Fact]
    public void IncludeArgs_RequiredInt_TooBig_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("big", FilterValue.From((long)int.MaxValue + 1L)));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredInt(include, "big"));
    }

    [Fact]
    public void IncludeArgs_RequiredInt_TooSmall_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("small", FilterValue.From((long)int.MinValue - 1L)));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredInt(include, "small"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_IntegerKind_Converts()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(5L)));
        Assert.Equal(5.0, ProjectionIncludeArguments.RequiredDouble(include, "d"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_NumberKind_ReturnsValue()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(3.14)));
        Assert.Equal(3.14, ProjectionIncludeArguments.RequiredDouble(include, "d"), 10);
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_UnsignedIntegerKind_Converts()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(100UL)));
        Assert.Equal(100.0, ProjectionIncludeArguments.RequiredDouble(include, "d"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_DecimalKind_Converts()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(2.5m)));
        Assert.Equal(2.5, ProjectionIncludeArguments.RequiredDouble(include, "d"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_MissingArg_Throws()
        => Assert.Throws<FilterValidationException>(() =>
            ProjectionIncludeArguments.RequiredDouble(MakeInclude("op"), "d"));

    [Fact]
    public void IncludeArgs_RequiredDouble_StringKind_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From("bad")));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredDouble(include, "d"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_BoolKind_Throws()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(true)));
        Assert.Throws<FilterValidationException>(() => ProjectionIncludeArguments.RequiredDouble(include, "d"));
    }

    [Fact]
    public void IncludeArgs_RequiredDouble_CustomErrorFactory_OnWrongType()
    {
        var include = MakeInclude("op", new EventProjectionArgument("d", FilterValue.From(true)));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProjectionIncludeArguments.RequiredDouble(include, "d", msg => new InvalidOperationException(msg)));
        Assert.Contains("must be a number", ex.Message);
    }
}

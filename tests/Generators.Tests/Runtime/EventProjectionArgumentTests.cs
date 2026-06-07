using SiftQL.Expressions;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class EventProjectionArgumentTests
{
    [Fact]
    public void Argument_From_Bool()
    {
        EventProjectionArgument arg = EventProjectionArgument.From("flag", true);
        Assert.Equal("flag", arg.Name);
        Assert.Equal(FilterValueKind.Boolean, arg.Value.Kind);
    }

    [Fact]
    public void Argument_From_Long()
    {
        EventProjectionArgument arg = EventProjectionArgument.From("limit", 100L);
        Assert.Equal(FilterValueKind.Integer, arg.Value.Kind);
    }

    [Fact]
    public void Argument_From_Double()
    {
        EventProjectionArgument arg = EventProjectionArgument.From("threshold", 0.5);
        Assert.Equal(FilterValueKind.Number, arg.Value.Kind);
    }

    [Fact]
    public void Argument_From_String()
    {
        EventProjectionArgument arg = EventProjectionArgument.From("tag", "vip");
        Assert.Equal(FilterValueKind.String, arg.Value.Kind);
        Assert.Equal("vip", arg.Value.String);
    }

    [Fact]
    public void Argument_From_Guid()
    {
        var guid = Guid.NewGuid();
        EventProjectionArgument arg = EventProjectionArgument.From("id", guid);
        Assert.Equal(FilterValueKind.Guid, arg.Value.Kind);
        Assert.Equal(guid, arg.Value.Guid);
    }

    [Fact]
    public void Argument_Constructor_ThrowsOnNullName()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new EventProjectionArgument(null!, FilterValue.From(1L)));
    }

    [Fact]
    public void Argument_DefaultConstructor_HasEmptyName()
    {
        var arg = new EventProjectionArgument();
        Assert.Equal(string.Empty, arg.Name);
        Assert.Equal(FilterValueKind.Null, arg.Value.Kind);
    }
}

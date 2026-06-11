using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class CollectionHiddenMemberProjectionRegressionTests
{
    [Fact]
    public void CollectionDerivedFieldUsesDeclaredElementMemberWhenRuntimeSubtypeHidesProperty()
    {
        FilterSchema.RegisterValueObject(typeof(BaseItem));
        FilterSchema schema = FilterSchema.For(typeof(ItemBag));
        Assert.True(schema.TryGetField("Items.Code", out FilterField field));
        var bag = new ItemBag([new DerivedItem("base-code", 7)]);

        object?[] values = Assert.IsType<object?[]>(field.Getter(bag));
        Assert.Equal(["base-code"], values);

        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ItemBag),
            FilterExpression.Contains("Items.Code", FilterValue.From("base-code")),
            FilterCompilerOptions.Immediate);
        Assert.True(kernel.Matches(bag));
    }

    private sealed record ItemBag(BaseItem[] Items) : IFilterSubject;

    private class BaseItem(string code)
    {
        public string Code { get; } = code;
    }

    private sealed class DerivedItem(string baseCode, int code) : BaseItem(baseCode)
    {
        public new int Code { get; } = code;
    }
}

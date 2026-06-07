using System.Linq.Expressions;
using SiftQL.Expressions;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class KernelExpressionEvaluatorDirectTests
{
    [Fact]
    public void Evaluate_ConstantExpression()
    {
        var param = Expression.Parameter(typeof(object), "x");
        var constant = Expression.Constant(42);
        object? result = KernelExpressionEvaluator.Evaluate(constant, param);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Evaluate_StaticFieldMember()
    {
        var param = Expression.Parameter(typeof(object), "x");
        var member = Expression.Field(null, typeof(StaticTestValues), nameof(StaticTestValues.IntField));
        object? result = KernelExpressionEvaluator.Evaluate(member, param);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Evaluate_StaticPropertyMember()
    {
        var param = Expression.Parameter(typeof(object), "x");
        var member = Expression.Property(null, typeof(StaticTestValues), nameof(StaticTestValues.IntProperty));
        object? result = KernelExpressionEvaluator.Evaluate(member, param);
        Assert.Equal(99, result);
    }

    [Fact]
    public void Evaluate_InstanceMemberOnCapturedClosure()
    {
        int captured = 77;
        Expression<Func<object, int>> lambda = _ => captured;
        var body = lambda.Body;
        var param = lambda.Parameters[0];
        object? result = KernelExpressionEvaluator.Evaluate(body, param);
        Assert.Equal(77, result);
    }

    [Fact]
    public void Evaluate_NewArrayExpression()
    {
        var param = Expression.Parameter(typeof(object), "x");
        var array = Expression.NewArrayInit(typeof(int),
            Expression.Constant(1),
            Expression.Constant(2),
            Expression.Constant(3));
        object? result = KernelExpressionEvaluator.Evaluate(array, param);
        Assert.IsType<int[]>(result);
        Assert.Equal([1, 2, 3], (int[])result!);
    }

    [Fact]
    public void Evaluate_ConvertWrapped_Unwraps()
    {
        var param = Expression.Parameter(typeof(object), "x");
        var inner = Expression.Constant(42);
        var convert = Expression.Convert(inner, typeof(long));
        object? result = KernelExpressionEvaluator.Evaluate(convert, param);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Evaluate_ParameterReference_Throws()
    {
        var param = Expression.Parameter(typeof(int), "x");
        Assert.Throws<KernelExpressionException>(
            () => KernelExpressionEvaluator.Evaluate(param, param));
    }

    [Fact]
    public void Evaluate_UnsupportedExpression_Throws()
    {
        var param = Expression.Parameter(typeof(int), "x");
        var add = Expression.Add(Expression.Constant(1), Expression.Constant(2));
        Assert.Throws<KernelExpressionException>(
            () => KernelExpressionEvaluator.Evaluate(add, param));
    }

    [Fact]
    public void EvaluateValue_ReturnsFilterValueWithParameterKey()
    {
        var param = Expression.Parameter(typeof(object), "x");
        var constant = Expression.Constant(42);
        FilterValue result = KernelExpressionEvaluator.EvaluateValue(constant, param, "p0");
        Assert.Equal(FilterValueKind.Integer, result.Kind);
        Assert.Equal("p0", result.ParameterKey);
    }

    public static class StaticTestValues
    {
        public static readonly int IntField = 42;
        public static int IntProperty => 99;
    }
}

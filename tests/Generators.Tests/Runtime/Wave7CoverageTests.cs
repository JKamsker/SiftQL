using System.Linq.Expressions;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Translation;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class Wave7CoverageTests
{
    #region ProjectedEventValue (65.7% → target 90%+)

    [Fact]
    public void FromScalar_Null_ReturnsNull()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(null);
        Assert.Equal(ProjectedEventValueKind.Null, value.Kind);
    }

    [Fact]
    public void FromScalar_Bool_ReturnsBoolean()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(true);
        Assert.Equal(ProjectedEventValueKind.Boolean, value.Kind);
        Assert.True(value.Boolean);
    }

    [Fact]
    public void FromScalar_Byte_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar((byte)42);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(42, value.Integer);
    }

    [Fact]
    public void FromScalar_SByte_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar((sbyte)-7);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(-7, value.Integer);
    }

    [Fact]
    public void FromScalar_Short_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar((short)1000);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(1000, value.Integer);
    }

    [Fact]
    public void FromScalar_UShort_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar((ushort)60000);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(60000, value.Integer);
    }

    [Fact]
    public void FromScalar_UInt_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(42u);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(42, value.Integer);
    }

    [Fact]
    public void FromScalar_ULong_SmallValue_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(100UL);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(100, value.Integer);
    }

    [Fact]
    public void FromScalar_ULong_LargeValue_ReturnsUnsignedInteger()
    {
        ulong large = (ulong)long.MaxValue + 1;
        ProjectedEventValue value = ProjectedEventValue.FromScalar(large);
        Assert.Equal(ProjectedEventValueKind.UnsignedInteger, value.Kind);
        Assert.Equal(large, value.UnsignedInteger);
    }

    [Fact]
    public void FromScalar_Float_ReturnsNumber()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(3.14f);
        Assert.Equal(ProjectedEventValueKind.Number, value.Kind);
        Assert.Equal(3.14f, value.Number, precision: 5);
    }

    [Fact]
    public void FromScalar_Double_ReturnsNumber()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(2.718);
        Assert.Equal(ProjectedEventValueKind.Number, value.Kind);
        Assert.Equal(2.718, value.Number);
    }

    [Fact]
    public void FromScalar_Decimal_Fractional_ReturnsDecimal()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(99.99m);
        Assert.Equal(ProjectedEventValueKind.Decimal, value.Kind);
    }

    [Fact]
    public void FromScalar_Decimal_WholeNumber_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(42m);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(42, value.Integer);
    }

    [Fact]
    public void FromScalar_Guid_ReturnsGuid()
    {
        var guid = Guid.NewGuid();
        ProjectedEventValue value = ProjectedEventValue.FromScalar(guid);
        Assert.Equal(ProjectedEventValueKind.Guid, value.Kind);
        Assert.Equal(guid, value.Guid);
    }

    [Fact]
    public void FromScalar_Enum_ReturnsString()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(DayOfWeek.Friday);
        Assert.Equal(ProjectedEventValueKind.String, value.Kind);
        Assert.Equal("Friday", value.String);
    }

    [Fact]
    public void FromScalar_Array_ReturnsArray()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(new[] { 1, 2, 3 });
        Assert.Equal(ProjectedEventValueKind.Array, value.Kind);
        Assert.Equal(3, value.Values.Length);
        Assert.Equal(1, value.Values[0].Integer);
    }

    [Fact]
    public void FromScalar_Object_ReturnsObjectWithFields()
    {
        var obj = new SimpleObj { Name = "test", Value = 42 };
        ProjectedEventValue value = ProjectedEventValue.FromScalar(obj);
        Assert.Equal(ProjectedEventValueKind.Object, value.Kind);
        Assert.True(value.Fields.Length >= 2);
    }

    [Fact]
    public void FromObject_Null_ReturnsNull()
    {
        ProjectedEventValue value = ProjectedEventValue.FromObject(null);
        Assert.Equal(ProjectedEventValueKind.Null, value.Kind);
    }

    [Fact]
    public void FromObject_ReturnsObjectKind()
    {
        ProjectedEventValue value = ProjectedEventValue.FromObject(new SimpleObj { Name = "x" });
        Assert.Equal(ProjectedEventValueKind.Object, value.Kind);
    }

    [Fact]
    public void FromArray_Collection_ReturnsArray()
    {
        var list = new List<int> { 10, 20 };
        ProjectedEventValue value = ProjectedEventValue.FromArray(list);
        Assert.Equal(ProjectedEventValueKind.Array, value.Kind);
        Assert.Equal(2, value.Values.Length);
    }

    [Fact]
    public void FromArray_NonCollection_ReturnsArray()
    {
        ProjectedEventValue value = ProjectedEventValue.FromArray(Generate());
        Assert.Equal(ProjectedEventValueKind.Array, value.Kind);
        Assert.Equal(3, value.Values.Length);

        static IEnumerable<object> Generate()
        {
            yield return 1;
            yield return "two";
            yield return 3.0;
        }
    }

    [Fact]
    public void FromArray_WithNulls_PreservesNullValues()
    {
        var items = new object?[] { 1, null, 3 };
        ProjectedEventValue value = ProjectedEventValue.FromArray(items);
        Assert.Equal(3, value.Values.Length);
        Assert.Equal(ProjectedEventValueKind.Null, value.Values[1].Kind);
    }

    [Fact]
    public void FromValues_ReturnsArrayOfProjectedValues()
    {
        var values = new[]
        {
            ProjectedEventValue.FromScalar(1),
            ProjectedEventValue.FromScalar("two"),
        };
        ProjectedEventValue result = ProjectedEventValue.FromValues(values);
        Assert.Equal(ProjectedEventValueKind.Array, result.Kind);
        Assert.Equal(2, result.Values.Length);
    }

    [Fact]
    public void FromFields_ReturnsObjectKind()
    {
        var fields = new[]
        {
            new ProjectedEventField("a", ProjectedEventValue.FromScalar(1)),
        };
        ProjectedEventValue result = ProjectedEventValue.FromFields(fields);
        Assert.Equal(ProjectedEventValueKind.Object, result.Kind);
        Assert.Single(result.Fields);
    }

    #endregion

    #region FilterValue (71.2% → target 90%+)

    [Fact]
    public void FilterValue_FromObject_Null()
    {
        FilterValue value = FilterValue.FromObject(null);
        Assert.Equal(FilterValueKind.Null, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_Bool()
    {
        FilterValue value = FilterValue.FromObject(true);
        Assert.Equal(FilterValueKind.Boolean, value.Kind);
        Assert.True(value.Boolean);
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((sbyte)-1)]
    [InlineData((short)100)]
    [InlineData((ushort)200)]
    [InlineData(42)]
    [InlineData(42u)]
    [InlineData(42L)]
    public void FilterValue_FromObject_IntegerTypes(object val)
    {
        FilterValue value = FilterValue.FromObject(val);
        Assert.Equal(FilterValueKind.Integer, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_ULong_Large()
    {
        ulong large = (ulong)long.MaxValue + 1;
        FilterValue value = FilterValue.FromObject(large);
        Assert.Equal(FilterValueKind.UnsignedInteger, value.Kind);
        Assert.Equal(large, value.UnsignedInteger);
    }

    [Fact]
    public void FilterValue_FromObject_Float()
    {
        FilterValue value = FilterValue.FromObject(1.5f);
        Assert.Equal(FilterValueKind.Number, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_Double()
    {
        FilterValue value = FilterValue.FromObject(2.5);
        Assert.Equal(FilterValueKind.Number, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_Decimal_Fractional()
    {
        FilterValue value = FilterValue.FromObject(99.99m);
        Assert.Equal(FilterValueKind.Decimal, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_Decimal_Integral()
    {
        FilterValue value = FilterValue.FromObject(42m);
        Assert.Equal(FilterValueKind.Integer, value.Kind);
    }

    [Fact]
    public void FilterValue_FromObject_String()
    {
        FilterValue value = FilterValue.FromObject("hello");
        Assert.Equal(FilterValueKind.String, value.Kind);
        Assert.Equal("hello", value.String);
    }

    [Fact]
    public void FilterValue_FromObject_Guid()
    {
        var guid = Guid.NewGuid();
        FilterValue value = FilterValue.FromObject(guid);
        Assert.Equal(FilterValueKind.Guid, value.Kind);
        Assert.Equal(guid, value.Guid);
    }

    [Fact]
    public void FilterValue_FromObject_Enum_ReturnsString()
    {
        FilterValue value = FilterValue.FromObject(DayOfWeek.Monday);
        Assert.Equal(FilterValueKind.String, value.Kind);
        Assert.Equal("Monday", value.String);
    }

    [Fact]
    public void FilterValue_FromObject_UnsupportedType_Throws()
    {
        Assert.Throws<KernelExpressionException>(() => FilterValue.FromObject(DateTime.Now));
    }

    [Fact]
    public void FilterValue_From_ULong_Small_ReturnsInteger()
    {
        FilterValue value = FilterValue.From(100UL);
        Assert.Equal(FilterValueKind.Integer, value.Kind);
        Assert.Equal(100, value.Integer);
    }

    [Fact]
    public void FilterValue_From_Decimal_WholeNumber_ReturnsInteger()
    {
        FilterValue value = FilterValue.From(7m);
        Assert.Equal(FilterValueKind.Integer, value.Kind);
        Assert.Equal(7, value.Integer);
    }

    #endregion

    #region SubscriptionIdBatch (70.4% → target 90%+)

    [Fact]
    public void SubscriptionIdBatch_One_AccessFirstSlot()
    {
        SubscriptionIdBatch batch = SubscriptionIdBatch.One("sub-1");
        Assert.Equal(1, batch.Count);
        Assert.Equal("sub-1", batch[0]);
    }

    [Fact]
    public void SubscriptionIdBatch_FourSlots_AccessAll()
    {
        var batch = new SubscriptionIdBatch(4, "a", "b", "c", "d");
        Assert.Equal("a", batch[0]);
        Assert.Equal("b", batch[1]);
        Assert.Equal("c", batch[2]);
        Assert.Equal("d", batch[3]);
    }

    [Fact]
    public void SubscriptionIdBatch_Overflow_AccessOverflowSlot()
    {
        var batch = new SubscriptionIdBatch(6, "a", "b", "c", "d", ["e", "f"]);
        Assert.Equal("e", batch[4]);
        Assert.Equal("f", batch[5]);
    }

    [Fact]
    public void SubscriptionIdBatch_OutOfRange_Throws()
    {
        var batch = SubscriptionIdBatch.One("sub-1");
        Assert.Throws<ArgumentOutOfRangeException>(() => batch[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => batch[-1]);
    }

    [Fact]
    public void SubscriptionIdBatch_ToArray_ReturnsAllIds()
    {
        var batch = new SubscriptionIdBatch(6, "a", "b", "c", "d", ["e", "f"]);
        string[] array = batch.ToArray();
        Assert.Equal(["a", "b", "c", "d", "e", "f"], array);
    }

    [Fact]
    public void SubscriptionIdBatch_OverflowMissing_ThrowsOnAccess()
    {
        var batch = new SubscriptionIdBatch(5, "a", "b", "c", "d", Overflow: null);
        Assert.Throws<InvalidOperationException>(() => batch[4]);
    }

    #endregion

    #region EventPipelineExpression (68.9% → target 90%+)

    [Fact]
    public void AppendFilter_AnyFilter_ReturnsSame()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default;
        EventPipelineExpression result = pipeline.AppendFilter(FilterExpression.Any);
        Assert.Same(pipeline, result);
    }

    [Fact]
    public void AppendSourceFilter_AnyFilter_ReturnsSame()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default;
        EventPipelineExpression result = pipeline.AppendSourceFilter(FilterExpression.Any);
        Assert.Same(pipeline, result);
    }

    [Fact]
    public void AppendSourceFilter_NoProjection_AppendsLikeNormalFilter()
    {
        var filter = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        EventPipelineExpression pipeline = EventPipelineExpression.Default;
        EventPipelineExpression result = pipeline.AppendSourceFilter(filter);
        Assert.Single(result.Stages);
        Assert.Equal(EventPipelineStageKind.Filter, result.Stages[0].Kind);
    }

    [Fact]
    public void AppendSourceFilter_WithProjection_InsertsBeforeProjection()
    {
        var filter1 = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var filter2 = FilterExpression.Compare("Quantity", FilterOperator.Equal, FilterValue.From(2L));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default);

        EventPipelineExpression result = pipeline.AppendSourceFilter(filter2);

        Assert.Equal(2, result.Stages.Length);
        Assert.Equal(EventPipelineStageKind.Filter, result.Stages[0].Kind);
        Assert.Equal(EventPipelineStageKind.Projection, result.Stages[1].Kind);
    }

    [Fact]
    public void AppendOrMergeLastProjection_NoExistingProjection_Appends()
    {
        EventPipelineExpression pipeline = EventPipelineExpression.Default;
        var projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        EventPipelineExpression result = pipeline.AppendOrMergeLastProjection(projection);
        Assert.Single(result.Stages);
        Assert.Equal(EventPipelineStageKind.Projection, result.Stages[0].Kind);
    }

    [Fact]
    public void AppendOrMergeLastProjection_ExistingProjection_MergesFields()
    {
        var proj1 = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        var proj2 = EventProjectionExpression.Select(nameof(ItemUsedEvent.Quantity));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(proj1);

        EventPipelineExpression result = pipeline.AppendOrMergeLastProjection(proj2);

        Assert.Single(result.Stages);
        Assert.Equal(2, result.Stages[0].Projection.Fields.Length);
    }

    [Fact]
    public void AppendOrMergeLastProjection_LastStageIsFilter_Appends()
    {
        var filter = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        EventPipelineExpression pipeline = EventPipelineExpression.Default
            .AppendProjection(EventProjectionExpression.Default)
            .AppendFilter(filter);

        var proj = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        EventPipelineExpression result = pipeline.AppendOrMergeLastProjection(proj);

        Assert.Equal(3, result.Stages.Length);
    }

    [Fact]
    public void From_NullFilterAndProjection_ReturnsDefault()
    {
        EventPipelineExpression result = EventPipelineExpression.From(null, null);
        Assert.True(result.IsDefault);
    }

    [Fact]
    public void From_FilterAndProjection_CreatesPipeline()
    {
        var filter = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        EventPipelineExpression result = EventPipelineExpression.From(filter, projection);
        Assert.Equal(2, result.Stages.Length);
    }

    [Fact]
    public void From_AnyFilter_SkipsFilterStage()
    {
        var projection = EventProjectionExpression.Select(nameof(ItemUsedEvent.ItemId));
        EventPipelineExpression result = EventPipelineExpression.From(FilterExpression.Any, projection);
        Assert.Single(result.Stages);
        Assert.Equal(EventPipelineStageKind.Projection, result.Stages[0].Kind);
    }

    [Fact]
    public void IsDefault_True_ForNewPipeline()
    {
        Assert.True(EventPipelineExpression.Default.IsDefault);
    }

    [Fact]
    public void HasProjection_False_ForFilterOnly()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L)));
        Assert.False(pipeline.HasProjection);
    }

    #endregion

    #region EventProjectionArgument (68.8% → target 90%+)

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

    #endregion

    #region KernelExpressionEvaluator (62.2% → target 85%+)

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

    #endregion

    #region FilterExpressionCost (73.7% → target 100%)

    [Fact]
    public void Cost_Any_IsZero()
    {
        int cost = FilterExpressionCost.Estimate(FilterExpression.Any);
        Assert.Equal(0, cost);
    }

    [Fact]
    public void Cost_Compare_Equal_IsOne()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        Assert.Equal(1, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Compare_NotEqual_IsTwo()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.NotEqual, FilterValue.From(1L));
        Assert.Equal(2, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Compare_GreaterThan_IsTwo()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.GreaterThan, FilterValue.From(1L));
        Assert.Equal(2, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Exists_IsOne()
    {
        var expr = FilterExpression.Exists("ItemId");
        Assert.Equal(1, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_In_ScalesWithValues()
    {
        var values = Enumerable.Range(0, 5).Select(i => FilterValue.From((long)i)).ToArray();
        var expr = FilterExpression.In("ItemId", values);
        Assert.Equal(4 + 5, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_In_CapsAtSixteen()
    {
        var values = Enumerable.Range(0, 20).Select(i => FilterValue.From((long)i)).ToArray();
        var expr = FilterExpression.In("ItemId", values);
        Assert.Equal(4 + 16, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Contains_IsThirtyTwo()
    {
        var expr = FilterExpression.Contains("Items", FilterValue.From(1L));
        Assert.Equal(32, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Not_IncludesChildCost()
    {
        var inner = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var expr = FilterExpression.Not(inner);
        Assert.Equal(8 + 1, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_And_SumsChildren()
    {
        var child1 = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var child2 = FilterExpression.Compare("Quantity", FilterOperator.Equal, FilterValue.From(2L));
        var expr = FilterExpression.And(child1, child2);
        Assert.Equal(2, FilterExpressionCost.Estimate(expr));
    }

    [Fact]
    public void Cost_Or_AddsSixteenPlusChildren()
    {
        var child1 = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var child2 = FilterExpression.Compare("Quantity", FilterOperator.Equal, FilterValue.From(2L));
        var expr = FilterExpression.Or(child1, child2);
        Assert.Equal(16 + 2, FilterExpressionCost.Estimate(expr));
    }

    #endregion

    #region FilterCompilerOptions / ProjectionCompilerOptions validation

    [Fact]
    public void FilterOptions_Immediate_ReturnsDefaultPolicy()
    {
        var options = FilterCompilerOptions.Immediate;
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        var policy = options.CreateFilterPromotionPolicy(expr);
        Assert.Equal(0, policy.MinimumEvaluations);
    }

    [Fact]
    public void FilterOptions_NegativeAge_Throws()
    {
        var options = FilterCompilerOptions.Tiered with
        {
            TieredPromotionMinimumAge = TimeSpan.FromSeconds(-1),
        };
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreateFilterPromotionPolicy(expr));
    }

    [Fact]
    public void FilterOptions_ZeroEvaluations_Throws()
    {
        var options = FilterCompilerOptions.Tiered with
        {
            TieredPromotionMinimumEvaluations = 0,
        };
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreateFilterPromotionPolicy(expr));
    }

    [Fact]
    public void FilterOptions_ZeroQueueCapacity_Throws()
    {
        var options = FilterCompilerOptions.Tiered with
        {
            TieredPromotionQueueCapacity = 0,
        };
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreateFilterPromotionPolicy(expr));
    }

    [Fact]
    public void ProjectionOptions_Immediate_ReturnsDefaultPolicy()
    {
        var policy = ProjectionCompilerOptions.Immediate.CreatePromotionPolicy();
        Assert.Equal(0, policy.MinimumOperations);
    }

    [Fact]
    public void ProjectionOptions_NegativeAge_Throws()
    {
        var options = ProjectionCompilerOptions.Tiered with
        {
            TieredPromotionMinimumAge = TimeSpan.FromSeconds(-1),
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreatePromotionPolicy());
    }

    [Fact]
    public void ProjectionOptions_ZeroOperations_Throws()
    {
        var options = ProjectionCompilerOptions.Tiered with
        {
            TieredPromotionMinimumOperations = 0,
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreatePromotionPolicy());
    }

    [Fact]
    public void ProjectionOptions_ZeroQueueCapacity_Throws()
    {
        var options = ProjectionCompilerOptions.Tiered with
        {
            TieredPromotionQueueCapacity = 0,
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreatePromotionPolicy());
    }

    #endregion

    #region KernelParameterKeyRewriter (80.7% → target 90%+)

    [Fact]
    public void ParameterCount_NoParameters_ReturnsZero()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        Assert.Equal(0, KernelParameterKeyRewriter.ParameterCount(expr));
    }

    [Fact]
    public void ParameterCount_WithParameters_CountsDistinct()
    {
        var expr = FilterExpression.And(
            FilterExpression.Compare("ItemId", FilterOperator.Equal,
                FilterValue.From(1L) with { ParameterKey = "p0" }),
            FilterExpression.Compare("Quantity", FilterOperator.Equal,
                FilterValue.From(2L) with { ParameterKey = "p1" }));
        Assert.Equal(2, KernelParameterKeyRewriter.ParameterCount(expr));
    }

    [Fact]
    public void ParameterCount_Projection_CountsIncludeArguments()
    {
        var projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude("test.intrinsic", "result",
                [EventProjectionArgument.From("limit", 10L) with
                {
                    Value = FilterValue.From(10L) with { ParameterKey = "p0" },
                }]),
        ]);
        Assert.Equal(1, KernelParameterKeyRewriter.ParameterCount(projection));
    }

    [Fact]
    public void ParameterCount_Pipeline_CountsBothFilterAndProjectionKeys()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare("ItemId", FilterOperator.Equal,
                FilterValue.From(1L) with { ParameterKey = "p0" }))
            .AppendProjection(EventProjectionExpression.Default.WithIncludes(
            [
                new EventProjectionInclude("test.intrinsic", "result",
                    [EventProjectionArgument.From("limit", 10L) with
                    {
                        Value = FilterValue.From(10L) with { ParameterKey = "p1" },
                    }]),
            ]));
        Assert.Equal(2, KernelParameterKeyRewriter.ParameterCount(pipeline));
    }

    [Fact]
    public void ParameterOffset_ReturnsNextAvailableOffset()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare("ItemId", FilterOperator.Equal,
                FilterValue.From(1L) with { ParameterKey = "p2" }));
        Assert.Equal(3, KernelParameterKeyRewriter.ParameterOffset(pipeline));
    }

    [Fact]
    public void ParameterOffset_NonNumericKeys_IgnoredInOffset()
    {
        var pipeline = EventPipelineExpression.Default
            .AppendFilter(FilterExpression.Compare("ItemId", FilterOperator.Equal,
                FilterValue.From(1L) with { ParameterKey = "custom" }));
        Assert.Equal(0, KernelParameterKeyRewriter.ParameterOffset(pipeline));
    }

    [Fact]
    public void Rebase_FilterExpression_ShiftsParameterKeys()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal,
            FilterValue.From(1L) with { ParameterKey = "p0" });
        FilterExpression rebased = KernelParameterKeyRewriter.Rebase(expr, 5);
        Assert.Equal("p5", rebased.Value?.ParameterKey);
    }

    [Fact]
    public void Rebase_FilterExpression_ZeroOffset_ReturnsSame()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal,
            FilterValue.From(1L) with { ParameterKey = "p0" });
        FilterExpression rebased = KernelParameterKeyRewriter.Rebase(expr, 0);
        Assert.Same(expr, rebased);
    }

    [Fact]
    public void Rebase_FilterExpression_NoParameters_ReturnsSame()
    {
        var expr = FilterExpression.Compare("ItemId", FilterOperator.Equal, FilterValue.From(1L));
        FilterExpression rebased = KernelParameterKeyRewriter.Rebase(expr, 5);
        Assert.Same(expr, rebased);
    }

    [Fact]
    public void Rebase_ProjectionExpression_ShiftsParameterKeys()
    {
        var projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude("test.intrinsic", "result",
                [new EventProjectionArgument("limit",
                    FilterValue.From(10L) with { ParameterKey = "p0" })]),
        ]);
        EventProjectionExpression rebased = KernelParameterKeyRewriter.Rebase(projection, 3);
        Assert.Equal("p3", rebased.Includes[0].Arguments[0].Value.ParameterKey);
    }

    [Fact]
    public void Rebase_ProjectionExpression_ZeroOffset_ReturnsSame()
    {
        var projection = EventProjectionExpression.Default.WithIncludes(
        [
            new EventProjectionInclude("test.intrinsic", "result",
                [new EventProjectionArgument("limit",
                    FilterValue.From(10L) with { ParameterKey = "p0" })]),
        ]);
        EventProjectionExpression rebased = KernelParameterKeyRewriter.Rebase(projection, 0);
        Assert.Same(projection, rebased);
    }

    #endregion

    #region Test helpers

    public sealed class SimpleObj
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    public static class StaticTestValues
    {
        public static readonly int IntField = 42;
        public static int IntProperty => 99;
    }

    #endregion
}

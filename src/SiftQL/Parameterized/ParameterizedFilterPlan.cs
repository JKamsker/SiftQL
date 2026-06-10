using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Parameterized;

internal sealed class ParameterizedFilterPlan
{
    private readonly string[] _parameterKeys;
    private readonly ParameterizedFilterPlanNode _root;

    public ParameterizedFilterPlan(string[] parameterKeys, ParameterizedFilterPlanNode root)
    {
        _parameterKeys = parameterKeys;
        _root = root;
    }

    public KernelPredicate Bind(FilterExpression expression)
    {
        FilterValue[] values = FilterExpressionParameters.BindValues(expression, _parameterKeys);
        return KernelPredicate.FromObject(_root.Bind(values));
    }
}

internal abstract class ParameterizedFilterPlanNode
{
    public abstract Func<object, bool> Bind(FilterValue[] parameters);
}

internal sealed class ConstantFilterPlanNode(bool value) : ParameterizedFilterPlanNode
{
    public override Func<object, bool> Bind(FilterValue[] parameters) =>
        value ? static _ => true : static _ => false;
}

internal sealed class CompositeFilterPlanNode(
    ParameterizedFilterPlanNode[] children,
    bool and) : ParameterizedFilterPlanNode
{
    public override Func<object, bool> Bind(FilterValue[] parameters)
    {
        var bound = children.Select(child => child.Bind(parameters)).ToArray();
        return and ? MatchAll(bound) : MatchAny(bound);
    }

    private static Func<object, bool> MatchAll(Func<object, bool>[] children) =>
        subject =>
        {
            for (int i = 0; i < children.Length; i++)
            {
                if (!children[i](subject))
                    return false;
            }

            return true;
        };

    private static Func<object, bool> MatchAny(Func<object, bool>[] children) =>
        subject =>
        {
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i](subject))
                    return true;
            }

            return false;
        };
}

internal sealed class NotFilterPlanNode(ParameterizedFilterPlanNode child) : ParameterizedFilterPlanNode
{
    public override Func<object, bool> Bind(FilterValue[] parameters)
    {
        var bound = child.Bind(parameters);
        return subject => !bound(subject);
    }
}

internal sealed class CompareFilterPlanNode(
    FilterField field,
    FilterOperator op,
    FilterValueRef value,
    bool ignoreCase) : ParameterizedFilterPlanNode
{
    public override Func<object, bool> Bind(FilterValue[] parameters)
    {
        FilterValue bound = value.Get(parameters);
        return FilterTypedPredicates.TryCompileCompare(field, bound, op, ignoreCase) ??
            (subject => FilterValues.Compare(field.Getter(subject), bound, op, ignoreCase));
    }
}

internal sealed class ElemMatchFilterPlanNode(
    Func<object, System.Collections.IEnumerable?> getCollection,
    ParameterizedFilterPlanNode child) : ParameterizedFilterPlanNode
{
    private const int MaxRuntimeElements = 256;

    public override Func<object, bool> Bind(FilterValue[] parameters)
    {
        Func<object, bool> childPredicate = child.Bind(parameters);
        return subject =>
        {
            System.Collections.IEnumerable? collection = getCollection(subject);
            if (collection is null)
                return false;

            int seen = 0;
            foreach (object? element in collection)
            {
                if (++seen > MaxRuntimeElements)
                    throw new InvalidOperationException(
                        $"ElemMatch filters support at most {MaxRuntimeElements} elements.");
                if (element is not null && childPredicate(element))
                    return true;
            }

            return false;
        };
    }
}

internal sealed class BetweenFilterPlanNode(
    FilterField field,
    FilterValueRef lower,
    FilterValueRef upper) : ParameterizedFilterPlanNode
{
    public override Func<object, bool> Bind(FilterValue[] parameters)
    {
        FilterValue boundLower = lower.Get(parameters);
        FilterValue boundUpper = upper.Get(parameters);
        return subject =>
        {
            object? actual = field.Getter(subject);
            return FilterValues.Compare(actual, boundLower, FilterOperator.GreaterThanOrEqual) &&
                FilterValues.Compare(actual, boundUpper, FilterOperator.LessThanOrEqual);
        };
    }
}

internal sealed class CountFilterPlanNode(
    FilterField field,
    FilterOperator op,
    FilterValueRef value) : ParameterizedFilterPlanNode
{
    public override Func<object, bool> Bind(FilterValue[] parameters)
    {
        FilterValue bound = value.Get(parameters);
        return subject => FilterValues.Compare(FilterValues.Count(field.Getter(subject)), bound, op);
    }
}

internal sealed class InFilterPlanNode(
    FilterField field,
    FilterValueRef[] values) : ParameterizedFilterPlanNode
{
    public override Func<object, bool> Bind(FilterValue[] parameters)
    {
        FilterValue[] bound = values.Select(value => value.Get(parameters)).ToArray();
        return FilterTypedPredicates.TryCompileIn(field, bound) ??
            (subject => FilterValues.In(field.Getter(subject), bound));
    }
}

internal sealed class ExistsFilterPlanNode(FilterField field) : ParameterizedFilterPlanNode
{
    public override Func<object, bool> Bind(FilterValue[] parameters) =>
        subject => field.Getter(subject) is not null;
}

internal sealed class ContainsFilterPlanNode(
    FilterField field,
    FilterValueRef value) : ParameterizedFilterPlanNode
{
    public override Func<object, bool> Bind(FilterValue[] parameters)
    {
        FilterValue bound = value.Get(parameters);
        return FilterTypedArrayPredicates.TryCompileContains(field, bound) ??
            (subject => FilterValues.Contains(field.Getter(subject), bound));
    }
}

internal readonly struct FilterValueRef
{
    private readonly int _parameterIndex;
    private readonly FilterValue? _constant;

    private FilterValueRef(int parameterIndex, FilterValue? constant)
    {
        _parameterIndex = parameterIndex;
        _constant = constant;
    }

    public static FilterValueRef Create(
        FilterValue value,
        IReadOnlyDictionary<string, int> parameterIndexes) =>
        !string.IsNullOrWhiteSpace(value.ParameterKey)
            ? new FilterValueRef(parameterIndexes[value.ParameterKey], null)
            : new FilterValueRef(-1, value);

    public FilterValue Get(FilterValue[] parameters) =>
        _parameterIndex >= 0 ? parameters[_parameterIndex] : _constant!;
}

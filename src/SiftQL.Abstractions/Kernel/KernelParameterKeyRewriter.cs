using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Translation;

namespace SiftQL;

internal static class KernelParameterKeyRewriter
{
    public static int ParameterCount(FilterExpression expression) =>
        CollectFilterKeys(expression).Count;

    public static int ParameterCount(EventProjectionExpression projection)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < projection.Includes.Length; i++)
        {
            EventProjectionArgument[] arguments = projection.Includes[i].Arguments;
            for (int j = 0; j < arguments.Length; j++)
                AddKey(keys, arguments[j].Value);
        }

        return keys.Count;
    }

    public static int ParameterCount(EventPipelineExpression pipeline)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage stage = pipeline.Stages[i];
            if (stage.Kind == EventPipelineStageKind.Filter)
                CollectFilterKeys(stage.Filter, keys);
            else
                CollectProjectionKeys(stage.Projection, keys);
        }

        return keys.Count;
    }

    public static int ParameterOffset(EventPipelineExpression pipeline)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage stage = pipeline.Stages[i];
            if (stage.Kind == EventPipelineStageKind.Filter)
                CollectFilterKeys(stage.Filter, keys);
            else
                CollectProjectionKeys(stage.Projection, keys);
        }

        return NextParameterOffset(keys);
    }

    public static FilterExpression Rebase(FilterExpression expression, int offset)
    {
        if (offset == 0 || !CollectFilterKeys(expression).Any())
            return expression;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        return RebaseFilter(expression, offset, map);
    }

    public static EventProjectionExpression Rebase(EventProjectionExpression projection, int offset)
    {
        if (offset == 0 || ParameterCount(projection) == 0)
            return projection;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        return projection with
        {
            Includes = projection.Includes
                .Select(include => RebaseInclude(include, offset, map))
                .ToArray(),
        };
    }

    private static FilterExpression RebaseFilter(
        FilterExpression expression,
        int offset,
        Dictionary<string, string> map) =>
        expression with
        {
            Value = RebaseValue(expression.Value, offset, map),
            Values = expression.Values.Select(value => RebaseValue(value, offset, map)!).ToArray(),
            Children = expression.Children.Select(child => RebaseFilter(child, offset, map)).ToArray(),
        };

    private static EventProjectionInclude RebaseInclude(
        EventProjectionInclude include,
        int offset,
        Dictionary<string, string> map) =>
        include with
        {
            Arguments = include.Arguments
                .Select(argument => argument with { Value = RebaseValue(argument.Value, offset, map)! })
                .ToArray(),
        };

    private static FilterValue? RebaseValue(
        FilterValue? value,
        int offset,
        Dictionary<string, string> map)
    {
        if (string.IsNullOrWhiteSpace(value?.ParameterKey))
            return value;
        if (!map.TryGetValue(value.ParameterKey, out string? rebased))
        {
            rebased = "p" + (offset + map.Count);
            map.Add(value.ParameterKey, rebased);
        }

        return value with { ParameterKey = rebased };
    }

    private static HashSet<string> CollectFilterKeys(FilterExpression expression)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        CollectFilterKeys(expression, keys);
        return keys;
    }

    private static void CollectProjectionKeys(EventProjectionExpression projection, HashSet<string> keys)
    {
        for (int i = 0; i < projection.Includes.Length; i++)
        {
            EventProjectionArgument[] arguments = projection.Includes[i].Arguments;
            for (int j = 0; j < arguments.Length; j++)
                AddKey(keys, arguments[j].Value);
        }
    }

    private static void CollectFilterKeys(FilterExpression expression, HashSet<string> keys)
    {
        AddKey(keys, expression.Value);
        for (int i = 0; i < expression.Values.Length; i++)
            AddKey(keys, expression.Values[i]);
        for (int i = 0; i < expression.Children.Length; i++)
            CollectFilterKeys(expression.Children[i], keys);
    }

    private static void AddKey(HashSet<string> keys, FilterValue? value)
    {
        if (!string.IsNullOrWhiteSpace(value?.ParameterKey))
            keys.Add(value.ParameterKey);
    }

    private static int NextParameterOffset(HashSet<string> keys)
    {
        int max = -1;
        foreach (string key in keys)
        {
            if (key.Length < 2 || key[0] != 'p')
                continue;
            if (!int.TryParse(key[1..], out int ordinal))
                continue;
            if (ordinal > max)
                max = ordinal;
        }

        return max + 1;
    }
}

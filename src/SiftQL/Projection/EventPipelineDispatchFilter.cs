using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Projection;

internal static class EventPipelineDispatchFilter
{
    public static bool ReferencesProjectedFields(EventPipelineExpression? pipeline)
    {
        if (pipeline?.Stages is null)
            return false;

        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            EventPipelineStage? stage = pipeline.Stages[i];
            if (stage?.Kind == EventPipelineStageKind.Filter &&
                ReferencesProjectedFields(stage.Filter))
            {
                return true;
            }
        }

        return false;
    }

    public static FilterExpression? Prune(FilterExpression filter)
    {
        (FilterExpression? projected, _) = ProjectedFilter(filter);
        return projected?.Kind == FilterExpressionKind.Any ? null : projected;
    }

    private static (FilterExpression? Filter, bool FullyRepresented) ProjectedFilter(
        FilterExpression filter) =>
        filter.Kind switch
        {
            FilterExpressionKind.Any => (FilterExpression.Any, true),
            FilterExpressionKind.And => ProjectedAndFilter(filter),
            FilterExpressionKind.Or => ProjectedOrFilter(filter),
            FilterExpressionKind.Not => ProjectedNotFilter(filter),
            _ => ProjectedEventPaths.TrySplit(filter.Field, out _, out _)
                ? (filter, true)
                : (null, false),
        };

    private static (FilterExpression? Filter, bool FullyRepresented) ProjectedAndFilter(
        FilterExpression filter)
    {
        var children = new List<FilterExpression>();
        bool fullyRepresented = true;
        for (int i = 0; i < filter.Children.Length; i++)
        {
            (FilterExpression? child, bool childFullyRepresented) =
                ProjectedFilter(filter.Children[i]);
            fullyRepresented &= childFullyRepresented;
            if (child is null || child.Kind == FilterExpressionKind.Any)
                continue;

            children.Add(child);
        }

        return children.Count switch
        {
            0 => fullyRepresented ? (FilterExpression.Any, true) : (null, false),
            1 => (children[0], fullyRepresented),
            _ => (filter with { Children = children.ToArray() }, fullyRepresented),
        };
    }

    private static (FilterExpression? Filter, bool FullyRepresented) ProjectedOrFilter(
        FilterExpression filter)
    {
        var children = new List<FilterExpression>();
        for (int i = 0; i < filter.Children.Length; i++)
        {
            (FilterExpression? child, bool fullyRepresented) =
                ProjectedFilter(filter.Children[i]);
            if (!fullyRepresented || child is null)
                return (null, false);
            if (child.Kind == FilterExpressionKind.Any)
                return (FilterExpression.Any, true);

            children.Add(child);
        }

        return children.Count switch
        {
            0 => (null, false),
            1 => (children[0], true),
            _ => (filter with { Children = children.ToArray() }, true),
        };
    }

    private static (FilterExpression? Filter, bool FullyRepresented) ProjectedNotFilter(
        FilterExpression filter)
    {
        if (filter.Children.Length != 1)
            return (null, false);

        (FilterExpression? child, bool fullyRepresented) = ProjectedFilter(filter.Children[0]);
        return fullyRepresented && child is not null
            ? (filter with { Children = [child] }, true)
            : (null, false);
    }

    private static bool ReferencesProjectedFields(FilterExpression? expression)
    {
        if (expression is null)
            return false;
        if (ProjectedEventPaths.TrySplit(expression.Field, out _, out _))
            return true;
        if (expression.Children is null)
            return false;

        for (int i = 0; i < expression.Children.Length; i++)
        {
            if (ReferencesProjectedFields(expression.Children[i]))
                return true;
        }

        return false;
    }
}

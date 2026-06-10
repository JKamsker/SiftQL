using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL;

internal static class ContextProjectionPipeline
{
    public static EventPipelineExpression AddIncludes(
        EventPipelineExpression pipeline,
        IReadOnlyList<EventProjectionInclude> includes,
        IReadOnlyList<EventProjectionField>? sourceFields = null)
    {
        if (includes.Count == 0 && (sourceFields is null || sourceFields.Count == 0))
            return pipeline;

        int projectionIndex = Array.FindIndex(
            pipeline.Stages,
            static stage => stage.Kind == EventPipelineStageKind.Projection);
        if (projectionIndex < 0)
        {
            EventProjectionExpression projection = EventProjectionExpression.Default with
            {
                Fields = sourceFields?.ToArray() ?? [],
                Includes = includes.ToArray(),
            };
            return pipeline.AppendProjection(projection);
        }

        var stages = pipeline.Stages.ToArray();
        EventProjectionExpression previous = stages[projectionIndex].Projection;
        stages[projectionIndex] = new EventPipelineStage
        {
            Kind = EventPipelineStageKind.Projection,
            Projection = previous with
            {
                Fields = sourceFields is { Count: > 0 }
                    ? [.. previous.Fields, .. sourceFields]
                    : previous.Fields,
                Includes = includes.Count == 0
                    ? previous.Includes
                    : AppendMissingIncludes(previous.Includes, includes),
            },
        };

        return pipeline with { Stages = stages };
    }

    public static EventPipelineExpression AppendSelectorWithIncludes(
        EventPipelineExpression pipeline,
        EventProjectionExpression projection,
        bool projected)
    {
        IReadOnlyList<EventProjectionField> sourceFields = SelectorSourceFields(projection, projected);
        EventPipelineExpression withIncludes = AddIncludes(
            pipeline,
            projection.Includes,
            sourceFields);
        EventProjectionField[] finalFields =
        [
            .. projection.Fields.Select(field => FinalField(field, projected)),
            .. projection.Includes.Select(static include =>
                new EventProjectionField(ProjectedEventPaths.Context(include.ResultName), include.ResultName)),
        ];

        return withIncludes.AppendProjection(EventProjectionExpression.Default.WithFields(finalFields));
    }

    public static bool HasProjection(EventPipelineExpression pipeline) =>
        pipeline.Stages.Any(static stage => stage.Kind == EventPipelineStageKind.Projection);

    public static string ProjectedFieldName(EventPipelineExpression pipeline, string sourcePath)
    {
        return TryProjectedFieldName(pipeline, sourcePath, out string fieldName)
            ? fieldName
            : sourcePath;
    }

    public static bool TryProjectedFieldName(
        EventPipelineExpression pipeline,
        string sourcePath,
        out string fieldName)
    {
        string currentName = sourcePath;
        bool projected = false;
        bool available = false;
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            if (pipeline.Stages[i].Kind != EventPipelineStageKind.Projection)
                continue;

            EventProjectionExpression projection = pipeline.Stages[i].Projection;
            string currentPath = projected
                ? ProjectedPath(currentName)
                : sourcePath;
            if (TryProjectedFieldName(projection, currentPath, out string nextName))
            {
                currentName = nextName;
                available = true;
            }
            else
            {
                available = false;
            }

            projected = true;
        }

        fieldName = currentName;
        return available;
    }

    public static string ProjectedPath(EventPipelineExpression pipeline, string sourcePath) =>
        ProjectedEventPaths.Field(ProjectedFieldName(pipeline, sourcePath));

    private static bool TryProjectedFieldName(
        EventProjectionExpression projection,
        string path,
        out string fieldName)
    {
        for (int i = projection.Fields.Length - 1; i >= 0; i--)
        {
            EventProjectionField field = projection.Fields[i];
            if (TryMatchPath(field.Path, path, out string suffix))
            {
                fieldName = field.Name + suffix;
                return true;
            }
        }

        fieldName = string.Empty;
        return false;
    }

    private static bool TryMatchPath(string candidate, string path, out string suffix)
    {
        if (string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
        {
            suffix = string.Empty;
            return true;
        }

        if (path.Length > candidate.Length &&
            path[candidate.Length] == '.' &&
            path.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
        {
            suffix = path[candidate.Length..];
            return true;
        }

        suffix = string.Empty;
        return false;
    }

    private static EventProjectionInclude[] AppendMissingIncludes(
        IReadOnlyList<EventProjectionInclude> existing,
        IReadOnlyList<EventProjectionInclude> includes)
    {
        var merged = new List<EventProjectionInclude>(existing);
        for (int i = 0; i < includes.Count; i++)
        {
            if (!ContainsInclude(existing, includes[i]))
                merged.Add(includes[i]);
        }

        return merged.ToArray();
    }

    private static bool ContainsInclude(
        IReadOnlyList<EventProjectionInclude> existing,
        EventProjectionInclude include)
    {
        for (int i = 0; i < existing.Count; i++)
        {
            if (IncludesMatch(existing[i], include))
                return true;
        }

        return false;
    }

    private static bool IncludesMatch(EventProjectionInclude left, EventProjectionInclude right) =>
        string.Equals(left.Intrinsic, right.Intrinsic, StringComparison.Ordinal) &&
        string.Equals(left.ResultName, right.ResultName, StringComparison.OrdinalIgnoreCase) &&
        ArgumentsMatch(left.Arguments, right.Arguments);

    private static bool ArgumentsMatch(
        IReadOnlyList<EventProjectionArgument> left,
        IReadOnlyList<EventProjectionArgument> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Name, right[i].Name, StringComparison.OrdinalIgnoreCase) ||
                left[i].Kind != right[i].Kind ||
                !string.Equals(left[i].SourcePath, right[i].SourcePath, StringComparison.OrdinalIgnoreCase) ||
                !EqualityComparer<FilterValue>.Default.Equals(left[i].Value, right[i].Value))
            {
                return false;
            }
        }

        return true;
    }

    private static EventProjectionField FinalField(EventProjectionField field, bool projected) =>
        projected
            ? field
            : new EventProjectionField(ProjectedEventPaths.Field(field.Name), field.Name);

    private static string ProjectedPath(string name) =>
        ProjectedEventPaths.TrySplit(name, out _, out _)
            ? name
            : ProjectedEventPaths.Field(name);

    private static IReadOnlyList<EventProjectionField> SelectorSourceFields(
        EventProjectionExpression projection,
        bool projected)
    {
        if (projected)
            return Array.Empty<EventProjectionField>();
        if (projection.Fields.Length != 0 || projection.Includes.Length == 0)
            return projection.Fields;

        return [new EventProjectionField("subjectType", "__siftqlSelectorSource")];
    }
}

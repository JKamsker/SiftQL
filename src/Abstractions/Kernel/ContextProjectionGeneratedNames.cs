using System.Globalization;
using SiftQL.Expressions;

namespace SiftQL;

internal static class ContextProjectionGeneratedNames
{
    private const string Prefix = "__ctx";

    public static int NextIndex(EventPipelineExpression pipeline)
    {
        int next = 0;
        for (int i = 0; i < pipeline.Stages.Length; i++)
        {
            if (pipeline.Stages[i].Kind == EventPipelineStageKind.Projection)
                next = NextIndex(pipeline.Stages[i].Projection.Includes, next);
        }

        return next;
    }

    public static int NextIndex(IReadOnlyList<ContextProjectionBinding> bindings, int minimum)
    {
        int next = minimum;
        for (int i = 0; i < bindings.Count; i++)
            next = NextIndex(bindings[i].Include.ResultName, next);
        return next;
    }

    public static string Format(int index) =>
        Prefix + index.ToString(CultureInfo.InvariantCulture);

    private static int NextIndex(IReadOnlyList<EventProjectionInclude> includes, int minimum)
    {
        int next = minimum;
        for (int i = 0; i < includes.Count; i++)
            next = NextIndex(includes[i].ResultName, next);
        return next;
    }

    private static int NextIndex(string? resultName, int minimum)
    {
        if (resultName is null ||
            !resultName.StartsWith(Prefix, StringComparison.Ordinal) ||
            !int.TryParse(resultName[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            return minimum;
        }

        return Math.Max(minimum, index + 1);
    }
}

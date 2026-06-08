using SiftQL.Projected;

namespace SiftQL.Projection;

internal static class ProjectionContextMerger
{
    public static ProjectedEventField[] Merge(
        IReadOnlyList<ProjectedEventField> inherited,
        IReadOnlyList<ProjectedEventField> includes)
    {
        if (inherited.Count == 0)
            return includes.ToArray();
        if (includes.Count == 0)
            return inherited.ToArray();

        var includeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < includes.Count; i++)
        {
            ProjectedEventField? field = includes[i];
            if (field is not null)
                includeNames.Add(field.Name);
        }

        var merged = new List<ProjectedEventField>(inherited.Count + includes.Count);
        for (int i = 0; i < inherited.Count; i++)
        {
            ProjectedEventField? field = inherited[i];
            if (field is not null && !includeNames.Contains(field.Name))
                merged.Add(field);
        }

        for (int i = 0; i < includes.Count; i++)
        {
            ProjectedEventField? field = includes[i];
            if (field is not null)
                merged.Add(field);
        }

        return merged.ToArray();
    }
}

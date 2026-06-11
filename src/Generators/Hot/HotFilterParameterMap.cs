namespace SiftQL.Generators.Hot;

internal sealed class HotFilterParameterMap
{
    private readonly Dictionary<string, int> _indexes;

    private HotFilterParameterMap(Dictionary<string, int> indexes)
    {
        _indexes = indexes;
    }

    public bool HasParameters => _indexes.Count != 0;

    public static HotFilterParameterMap Create(HotFilterNode node)
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        Visit(node, indexes);
        return new HotFilterParameterMap(indexes);
    }

    public int IndexOf(HotFilterValue value) =>
        ParameterKey(value) is { } key &&
            _indexes.TryGetValue(key, out int index)
            ? index
            : throw new InvalidOperationException(
                $"Hot filter parameter '{value.ParameterKey}' was not indexed.");

    private static void Visit(HotFilterNode node, Dictionary<string, int> indexes)
    {
        switch (node.Kind)
        {
            case HotFilterNodeKind.Compare:
            case HotFilterNodeKind.Contains:
                if (node.Value is not null)
                    Add(node.Value, indexes);
                break;
            case HotFilterNodeKind.In:
                for (int i = 0; i < node.Values.Count; i++)
                    Add(node.Values[i], indexes);
                break;
            case HotFilterNodeKind.And:
            case HotFilterNodeKind.Or:
            case HotFilterNodeKind.Not:
                for (int i = 0; i < node.Children.Count; i++)
                    Visit(node.Children[i], indexes);
                break;
        }
    }

    private static void Add(HotFilterValue value, Dictionary<string, int> indexes)
    {
        string? key = ParameterKey(value);
        if (key is null || indexes.ContainsKey(key))
        {
            return;
        }

        indexes.Add(key, indexes.Count);
    }

    private static string? ParameterKey(HotFilterValue value) =>
        string.IsNullOrWhiteSpace(value.ParameterKey) ? null : value.ParameterKey;
}

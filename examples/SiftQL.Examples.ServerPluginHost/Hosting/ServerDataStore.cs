using SiftQL;

namespace SiftQL.Examples.ServerPluginHost.Hosting;

public sealed class ServerDataStore
{
    private readonly Dictionary<Type, List<object>> _rows = [];

    public void Replace<TModel>(IEnumerable<TModel> rows)
        where TModel : IFilterSubject
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows[typeof(TModel)] = rows.Cast<object>().ToList();
    }

    public IReadOnlyList<TModel> Rows<TModel>()
        where TModel : IFilterSubject
    {
        if (!_rows.TryGetValue(typeof(TModel), out List<object>? rows))
            return [];

        return rows.Cast<TModel>().ToArray();
    }
}

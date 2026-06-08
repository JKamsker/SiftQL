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
        var results = new List<TModel>();
        foreach (List<object> rows in _rows.Values)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] is TModel row)
                    results.Add(row);
            }
        }

        return results;
    }
}

using SiftQL.Examples.ShaRpc.SharedContracts.Domain;

namespace SiftQL.Examples.ShaRpc.Server.Hosting;

public sealed class ServerDataStore
{
    private readonly Dictionary<Type, List<object>> _rows = [];

    public void Replace<TRecord>(IEnumerable<TRecord> rows)
        where TRecord : IServerRecord
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows[typeof(TRecord)] = rows.Cast<object>().ToList();
    }

    public IReadOnlyList<object> Rows(Type subjectType)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        var matches = new List<object>();
        foreach (List<object> rows in _rows.Values)
        {
            foreach (object row in rows)
            {
                if (subjectType.IsInstanceOfType(row))
                    matches.Add(row);
            }
        }

        return matches;
    }
}

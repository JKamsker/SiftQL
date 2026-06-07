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
        return _rows.TryGetValue(subjectType, out List<object>? rows)
            ? rows
            : [];
    }
}

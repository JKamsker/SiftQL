using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;

namespace SiftQL.Examples.ShaRpc.Server.Hosting;

public sealed class ClientMessageSink
{
    private readonly object _gate = new();
    private readonly List<ClientDelivery> _deliveries = [];

    public IReadOnlyList<ClientDelivery> Deliveries
    {
        get
        {
            lock (_gate)
                return _deliveries.ToArray();
        }
    }

    public void Add(ClientDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        lock (_gate)
            _deliveries.Add(delivery);
    }
}

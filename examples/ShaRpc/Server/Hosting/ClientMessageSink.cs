using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;

namespace SiftQL.Examples.ShaRpc.Server.Hosting;

public sealed class ClientMessageSink
{
    private readonly object _gate = new();
    private readonly List<ClientDelivery> _deliveries = [];
    private readonly HashSet<string> _deliveryIds = new(StringComparer.Ordinal);

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
        {
            if (!string.IsNullOrEmpty(delivery.DeliveryId) &&
                !_deliveryIds.Add(delivery.DeliveryId))
            {
                return;
            }

            _deliveries.Add(delivery);
        }
    }
}

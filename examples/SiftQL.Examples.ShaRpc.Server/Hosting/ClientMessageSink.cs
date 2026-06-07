using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;

namespace SiftQL.Examples.ShaRpc.Server.Hosting;

public sealed class ClientMessageSink
{
    private readonly List<ClientDelivery> _deliveries = [];

    public IReadOnlyList<ClientDelivery> Deliveries => _deliveries;

    public void Add(ClientDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        _deliveries.Add(delivery);
    }
}

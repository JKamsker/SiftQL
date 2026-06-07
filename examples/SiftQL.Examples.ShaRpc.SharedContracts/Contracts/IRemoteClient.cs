using ShaRPC.Core.Attributes;
using SiftQL.Projected;

namespace SiftQL.Examples.ShaRpc.SharedContracts.Contracts;

[ShaRpcService]
public interface IRemoteClient
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task DispatchAsync(
        SubscriptionDispatch dispatch,
        CancellationToken cancellationToken = default);
}

public sealed record SubscriptionDispatch(
    string SubscriptionId,
    string Subject,
    ProjectedEvent Payload);

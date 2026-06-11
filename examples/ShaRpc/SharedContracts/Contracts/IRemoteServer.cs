using ShaRPC.Core.Attributes;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Examples.ShaRpc.SharedContracts.Contracts;

[ShaRpcService]
public interface IRemoteServer
{
    Task<ServerHello> HelloAsync(
        ClientHello hello,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectedEvent>> QueryAsync(
        ServerQueryRequest request,
        CancellationToken cancellationToken = default);

    Task SubscribeAsync(
        SubscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task SendToClientAsync(
        ClientDelivery delivery,
        CancellationToken cancellationToken = default);
}

public sealed record ClientHello(
    string ClientId,
    string[] KnownSubjects);

public sealed record ServerHello(
    string ServerName,
    string[] Subjects,
    int OnlineClientCount);

public sealed record ServerQueryRequest(
    string Subject,
    EventPipelineExpression Pipeline);

public sealed record SubscriptionRequest(
    string SubscriptionId,
    string Subject,
    EventPipelineExpression Pipeline);

public sealed record ClientDelivery(
    long ClientId,
    string Channel,
    ProjectedEvent Payload,
    string? DeliveryId = null);

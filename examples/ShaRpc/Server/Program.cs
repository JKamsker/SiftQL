using System.Globalization;
using System.IO.Pipes;
using ShaRPC.Core;
using ShaRPC.Core.Transport;
using SiftQL.Examples.ShaRpc.Server.Hosting;
using SiftQL.Examples.ShaRpc.Server.Transport;
using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Domain;
using SiftQL.Examples.ShaRpc.SharedContracts.Serialization;
using SiftQL.Projected;

var dataStore = new ServerDataStore();
dataStore.Replace<ServerOfferSnapshot>(
[
    new("north-gate", "field-kit", "potion", 45, 3, true),
    new("north-gate", "vault-kit", "crystal", 75, 2, true),
    new("north-gate", "closed-cache", "token", 15, 6, false),
    new("harbor", "dock-kit", "potion", 35, 4, true),
]);

var clientMessages = new ClientMessageSink();
var queryContext = new ServerLookupContext(
    new(1001, "Ari", ClientTier.Premium),
    new(1002, "Bryn", ClientTier.Standard));
var server = new RemoteServerService(dataStore, clientMessages, queryContext);
string pipeName = $"siftql-sharpc-example-{Guid.NewGuid():N}";

await using var pipe = new NamedPipeServerStream(
    pipeName,
    PipeDirection.InOut,
    maxNumberOfServerInstances: 1,
    PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous);
await using ClientProcess clientProcess = ClientProcess.Start(pipeName);

await pipe.WaitForConnectionAsync().ConfigureAwait(false);
await using var peer = RpcPeer.Over(
        new StreamConnection(pipe, "server"),
        JsonRpcSerializerFactory.Create(),
        new RpcPeerOptions
        {
            MaxConcurrentInboundDispatch = 4,
            RequestTimeout = TimeSpan.FromSeconds(15),
        })
    .Provide<IRemoteServer>(server)
    .Start();

IRemoteClient client = peer.Get<IRemoteClient>();
server.Attach(client);
await client.StartAsync().ConfigureAwait(false);

await server.PublishAsync(new InventoryChangedEvent(1001, "north-gate", "potion", 2, 18))
    .ConfigureAwait(false);
await server.PublishAsync(new InventoryChangedEvent(1002, "north-gate", "ore", 1, 18))
    .ConfigureAwait(false);
await server.PublishAsync(new InventoryChangedEvent(1001, "harbor", "potion", 4, 18))
    .ConfigureAwait(false);

await peer.CloseAsync(CancellationToken.None).ConfigureAwait(false);
await clientProcess.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

Console.WriteLine();
Console.WriteLine("Server recorded client deliveries:");
foreach (ClientDelivery delivery in clientMessages.Deliveries)
{
    Console.WriteLine(
        $"  client={delivery.ClientId} channel={delivery.Channel} payload={FormatPayload(delivery.Payload)}");
}

static string FormatPayload(ProjectedEvent payload) =>
    string.Join(", ", payload.Fields.Select(static field => $"{field.Name}={FormatValue(field.Value)}"));

static string FormatValue(ProjectedEventValue value) =>
    value.Kind switch
    {
        ProjectedEventValueKind.Null => "null",
        ProjectedEventValueKind.Boolean => value.Boolean.ToString(CultureInfo.InvariantCulture),
        ProjectedEventValueKind.Integer => value.Integer.ToString(CultureInfo.InvariantCulture),
        ProjectedEventValueKind.UnsignedInteger => value.UnsignedInteger.ToString(CultureInfo.InvariantCulture),
        ProjectedEventValueKind.Number => value.Number.ToString(CultureInfo.InvariantCulture),
        ProjectedEventValueKind.String => "\"" + value.String + "\"",
        ProjectedEventValueKind.Guid => value.Guid.ToString("D"),
        ProjectedEventValueKind.Array => "[" + string.Join(", ", value.Values.Select(FormatValue)) + "]",
        ProjectedEventValueKind.Object => "{" + string.Join(", ", value.Fields.Select(FormatField)) + "}",
        _ => value.ToString(),
    };

static string FormatField(ProjectedEventField field) =>
    $"{field.Name}: {FormatValue(field.Value)}";

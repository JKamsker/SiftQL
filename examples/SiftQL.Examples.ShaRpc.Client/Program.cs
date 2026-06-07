using System.IO.Pipes;
using ShaRPC.Core;
using ShaRPC.Core.Transport;
using SiftQL.Examples.ShaRpc.Client.Hosting;
using SiftQL.Examples.ShaRpc.SharedContracts.Contracts;
using SiftQL.Examples.ShaRpc.SharedContracts.Serialization;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: SiftQL.Examples.ShaRpc.Client <pipe-name>");
    return 2;
}

await using var pipe = new NamedPipeClientStream(
    ".",
    args[0],
    PipeDirection.InOut,
    PipeOptions.Asynchronous);
await pipe.ConnectAsync(timeout: 10_000).ConfigureAwait(false);

var service = new RemoteClientService();
var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
await using var peer = RpcPeer.Over(
        new StreamConnection(pipe, "client"),
        JsonRpcSerializerFactory.Create(),
        new RpcPeerOptions
        {
            MaxConcurrentInboundDispatch = 4,
            RequestTimeout = TimeSpan.FromSeconds(15),
        })
    .Provide<IRemoteClient>(service)
    .Start();

peer.Disconnected += (_, _) => disconnected.TrySetResult();
service.Attach(peer.Get<IRemoteServer>());
await disconnected.Task.ConfigureAwait(false);
return 0;

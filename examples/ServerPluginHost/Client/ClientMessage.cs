namespace SiftQL.Examples.ServerPluginHost.Client;

public sealed record ClientMessage(
    string Channel,
    IReadOnlyDictionary<string, object?> Payload);

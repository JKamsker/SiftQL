namespace SiftQL.Examples.ShaRpc.SharedContracts.Domain;

public sealed class ServerLookupContext
{
    private readonly Dictionary<long, ClientProfile> _clients;

    public ServerLookupContext(params ClientProfile[] clients)
    {
        _clients = clients.ToDictionary(static client => client.SessionId);
    }

    public ClientProfile GetClient(long sessionId) =>
        _clients.TryGetValue(sessionId, out ClientProfile? client) ? client : null!;
}

public sealed record ClientProfile(
    long SessionId,
    string DisplayName,
    ClientTier Tier);

public enum ClientTier
{
    Standard,
    Premium,
}

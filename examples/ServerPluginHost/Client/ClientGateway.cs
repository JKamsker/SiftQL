using SiftQL.Projected;

namespace SiftQL.Examples.ServerPluginHost.Client;

public sealed class ClientGateway
{
    private readonly Dictionary<long, ClientSession> _sessions = [];

    public IReadOnlyCollection<ClientSession> Sessions => _sessions.Values;

    public ClientSession Register(long avatarId)
    {
        if (_sessions.TryGetValue(avatarId, out ClientSession? existing))
            return existing;

        var session = new ClientSession(avatarId);
        _sessions.Add(avatarId, session);
        return session;
    }

    public bool SendToAvatar(long avatarId, string channel, ProjectedEvent projected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(projected);
        if (!_sessions.TryGetValue(avatarId, out ClientSession? session))
            return false;

        session.Receive(new ClientMessage(channel, ClientPayload.From(projected)));
        return true;
    }
}

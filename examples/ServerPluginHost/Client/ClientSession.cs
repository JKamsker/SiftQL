namespace SiftQL.Examples.ServerPluginHost.Client;

public sealed class ClientSession(long avatarId)
{
    private readonly List<ClientMessage> _messages = [];

    public long AvatarId { get; } = avatarId;

    public IReadOnlyList<ClientMessage> Messages => _messages;

    public void Receive(ClientMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Add(message);
    }
}

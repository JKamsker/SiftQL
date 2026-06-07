using System.Globalization;
using SiftQL.Examples.ServerPluginHost.Client;
using SiftQL.Examples.ServerPluginHost.Domain;
using SiftQL.Examples.ServerPluginHost.Hosting;
using SiftQL.Examples.ServerPluginHost.Plugins;

var clients = new ClientGateway();
clients.Register(1001);
clients.Register(1002);

var serverData = new ServerDataStore();
serverData.Replace<ServerOfferSnapshot>(
[
    new("north-gate", "field-kit", "potion", 45, 3, true),
    new("north-gate", "vault-kit", "crystal", 75, 2, true),
    new("north-gate", "closed-cache", "token", 15, 6, false),
    new("harbor", "dock-kit", "potion", 35, 4, true),
]);

var host = new InMemoryServerPluginHost(clients, serverData);

host.Register(new OfferLookupPlugin());
host.Register(new InventoryAuditPlugin());
host.Register(new EncounterMonitorPlugin());

await host.StartAsync();

ItemUsedEvent[] inventoryEvents =
[
    new(1001, "north-gate", "potion", "consumable", 2, 18),
    new(1002, "north-gate", "ore", "material", 1, 0),
    new(1001, "harbor", "potion", "consumable", 0, 18),
];

AbilityCastEvent[] castEvents =
[
    new(1001, "north-gate", "ember-lance", 140, true),
    new(1002, "harbor", "spark", 20, false),
];

foreach (ItemUsedEvent itemUsed in inventoryEvents)
    await host.PublishAsync(itemUsed);

foreach (AbilityCastEvent cast in castEvents)
    await host.PublishAsync(cast);

Console.WriteLine("Registered server models:");
foreach (Type type in ServerKernel.SubjectTypes)
    Console.WriteLine($"  {type.Name}");

Console.WriteLine();
Console.WriteLine("Client messages:");
foreach (ClientSession session in clients.Sessions.OrderBy(static item => item.AvatarId))
{
    Console.WriteLine($"  Client {session.AvatarId}:");
    if (session.Messages.Count == 0)
    {
        Console.WriteLine("    <none>");
        continue;
    }

    foreach (ClientMessage message in session.Messages)
        Console.WriteLine($"    {message.Channel}: {FormatPayload(message.Payload)}");
}

static string FormatPayload(IReadOnlyDictionary<string, object?> payload) =>
    string.Join(", ", payload.Select(static pair => $"{pair.Key}={FormatValue(pair.Value)}"));

static string FormatValue(object? value) =>
    value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        Array items => "[" + string.Join(", ", items.Cast<object?>().Select(FormatValue)) + "]",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

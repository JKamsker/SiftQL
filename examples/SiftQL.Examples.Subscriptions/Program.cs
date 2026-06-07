using SiftQL;
using SiftQL.Expressions;
using SiftQL.Index;

// Typed subscription index — routes events to matching subscribers
var index = new TypedFilterSubscriptionIndex<Subscription, SensorReading>();

// Subscriber 1: high-temperature alerts
var highTemp = new Subscription("high-temp-alert");
index.Add(highTemp, FilterExpression.Compare(
    "temperature", FilterOperator.GreaterThan, FilterValue.From(80.0)));

// Subscriber 2: pressure warnings in zone A
var pressureZoneA = new Subscription("pressure-zone-a");
index.Add(pressureZoneA, FilterExpression.And(
    FilterExpression.Compare("zone", FilterOperator.Equal, FilterValue.From("A")),
    FilterExpression.Compare("pressure", FilterOperator.GreaterThan, FilterValue.From(150.0))));

// Subscriber 3: all readings (no filter)
var allReadings = new Subscription("all-readings");
index.Add(allReadings, null);

// Fire some events and see which subscriptions match
var readings = new SensorReading[]
{
    new() { SensorId = "S1", Zone = "A", Temperature = 95.0, Pressure = 180.0 },
    new() { SensorId = "S2", Zone = "B", Temperature = 72.0, Pressure = 100.0 },
    new() { SensorId = "S3", Zone = "A", Temperature = 60.0, Pressure = 155.0 },
};

foreach (var reading in readings)
{
    Console.WriteLine($"\nSensor {reading.SensorId} (Zone={reading.Zone}, Temp={reading.Temperature}, Pressure={reading.Pressure}):");
    var matches = index.SnapshotCandidates(reading);
    foreach (var match in matches)
        Console.WriteLine($"  -> {match.Name}");
}

public sealed record Subscription(string Name);

public sealed record SensorReading : IFilterSubject
{
    public string SensorId { get; init; } = "";
    public string Zone { get; init; } = "";
    public double Temperature { get; init; }
    public double Pressure { get; init; }
}

namespace SiftQL;

[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class SiftQueryContextAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SiftQueryContextMethodAttribute(string? id = null) : Attribute
{
    public string? Id { get; } = id;
}

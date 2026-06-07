namespace SiftQL.Schema;

public sealed record FilterFieldAccess(string? PropertyPath, object? ConstantValue = null)
{
    public static FilterFieldAccess ForProperty(string propertyPath) =>
        new(propertyPath ?? throw new ArgumentNullException(nameof(propertyPath)));

    public static FilterFieldAccess ForConstant(object? value) =>
        new(null, value);
}

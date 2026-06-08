namespace SiftQL;

public sealed record SiftQueryContextDescriptor(
    Type ContextType,
    string ContextId,
    IReadOnlyList<SiftQueryContextMethodDescriptor> Methods);

public sealed record SiftQueryContextMethodDescriptor(
    string MethodName,
    string MethodId,
    Type ReturnType,
    IReadOnlyList<SiftQueryContextParameterDescriptor> Parameters);

public sealed record SiftQueryContextParameterDescriptor(
    string Name,
    Type Type,
    bool HasDefaultValue,
    object? DefaultValue);

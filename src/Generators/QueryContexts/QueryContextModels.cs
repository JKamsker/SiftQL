namespace SiftQL.Generators.QueryContexts;

internal sealed record QueryContextResult(
    QueryContextModel? Context,
    EquatableArray<QueryContextDiagnostic> Diagnostics);

internal sealed record QueryContextModel(
    string NamespaceName,
    string InterfaceName,
    string InterfaceTypeName,
    string ContextId,
    string HelperName,
    EquatableArray<QueryContextMethodModel> Methods);

internal sealed record QueryContextMethodModel(
    string Name,
    string MethodId,
    string ReturnTypeName,
    EquatableArray<QueryContextParameterModel> Parameters);

internal sealed record QueryContextParameterModel(
    string Name,
    string TypeName,
    bool HasDefaultValue,
    string DefaultValueSource);

internal sealed record QueryContextDiagnostic(string Id, string Message);

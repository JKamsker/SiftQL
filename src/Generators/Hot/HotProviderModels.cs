namespace SiftQL.Generators.Hot;

internal enum HotEntryKind
{
    Filter,
    Projection,
}

internal enum HotFilterNodeKind
{
    Any = 0,
    And = 1,
    Or = 2,
    Not = 3,
    Compare = 4,
    In = 5,
    Exists = 6,
    Contains = 7,
}

internal enum HotFilterValueKind
{
    Null = 0,
    Boolean = 1,
    Integer = 2,
    Number = 3,
    String = 4,
    Guid = 5,
    UnsignedInteger = 6,
}

internal sealed record HotManifestParseResult(
    string Path,
    string ProviderName,
    string HintName,
    string ManifestHash,
    EquatableArray<HotManifestEntry> Entries,
    EquatableArray<HotProviderDiagnostic> Diagnostics);

internal sealed record HotManifestEntry(
    HotEntryKind Kind,
    string SubjectType,
    string Fingerprint,
    HotFilterNode? Filter,
    HotProjection? Projection);

internal sealed record HotFilterNode(
    HotFilterNodeKind Kind,
    string Field,
    int Operator,
    HotFilterValue? Value,
    EquatableArray<HotFilterValue> Values,
    EquatableArray<HotFilterNode> Children);

internal sealed record HotFilterValue(
    HotFilterValueKind Kind,
    string? ParameterKey,
    bool Boolean,
    long Integer,
    ulong UnsignedInteger,
    double Number,
    string? String,
    string Guid);

internal sealed record HotProjection(
    EquatableArray<HotProjectionField> Fields,
    bool HasIncludes,
    bool HasParameters,
    EquatableArray<HotProjectionInclude> Includes);

internal sealed record HotProjectionField(string Name, string Path);

internal sealed record HotProjectionInclude(
    string Intrinsic,
    string ResultName,
    EquatableArray<HotProjectionArgument> Arguments);

internal sealed record HotProjectionArgument(string Name, HotFilterValue Value);

internal sealed record HotProviderEntry(
    HotEntryKind Kind,
    string SubjectTypeName,
    string Fingerprint,
    HotFilterNode? Filter,
    HotProjection? Projection);

internal sealed record HotProviderSource(
    string ProviderName,
    string HintName,
    string ManifestHash,
    EquatableArray<HotProviderEntry> Entries,
    EquatableArray<HotProviderDiagnostic> Diagnostics);

internal sealed record HotProviderDiagnostic(string Id, string Path, string Message);

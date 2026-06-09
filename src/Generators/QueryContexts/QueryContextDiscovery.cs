using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SiftQL.Generators.QueryContexts;

internal static class QueryContextDiscovery
{
    public const string ContextAttributeName = "SiftQL.SiftQueryContextAttribute";
    private const string MethodAttributeName = "SiftQL.SiftQueryContextMethodAttribute";
    private static readonly SymbolDisplayFormat s_typeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public static bool IsCandidate(SyntaxNode node, CancellationToken _) =>
        node is InterfaceDeclarationSyntax;

    public static QueryContextResult Discover(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = ImmutableArray.CreateBuilder<QueryContextDiagnostic>();
        var contract = (INamedTypeSymbol)context.TargetSymbol;
        string contextId = ContextId(context);
        string helperName = HelperName(contract.Name);

        ValidateContext(contract, contextId, helperName, diagnostics);
        EquatableArray<QueryContextMethodModel> methods = DiscoverMethods(
            contract,
            diagnostics,
            cancellationToken);

        if (diagnostics.Count != 0)
            return new(null, diagnostics.ToImmutable());

        return new(
            new QueryContextModel(
                NamespaceName(contract),
                AccessibilityText(contract),
                contract.Name,
                contract.ToDisplayString(s_typeFormat),
                contextId,
                helperName,
                methods),
            EquatableArray<QueryContextDiagnostic>.Empty);
    }

    private static void ValidateContext(
        INamedTypeSymbol contract,
        string contextId,
        string helperName,
        ImmutableArray<QueryContextDiagnostic>.Builder diagnostics)
    {
        if (contract is not { TypeKind: TypeKind.Interface, IsGenericType: false })
        {
            diagnostics.Add(new(
                QueryContextDiagnostics.InvalidContextShape,
                $"Query context '{contract.ToDisplayString()}' must be a non-generic interface."));
        }

        if (string.IsNullOrWhiteSpace(contextId))
        {
            diagnostics.Add(new(
                QueryContextDiagnostics.InvalidContextShape,
                $"Query context '{contract.ToDisplayString()}' must declare a non-empty context id."));
        }

        if (contract.ContainingNamespace.GetTypeMembers(helperName).Length != 0)
        {
            diagnostics.Add(new(
                QueryContextDiagnostics.HelperNameCollision,
                $"Query context helper '{helperName}' already exists in namespace '{NamespaceName(contract)}'."));
        }
    }

    private static EquatableArray<QueryContextMethodModel> DiscoverMethods(
        INamedTypeSymbol contract,
        ImmutableArray<QueryContextDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        var methods = ImmutableArray.CreateBuilder<QueryContextMethodModel>();
        var methodIds = new HashSet<string>(StringComparer.Ordinal);
        var includeFactorySignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (IMethodSymbol method in contract.GetMembers().OfType<IMethodSymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (method.MethodKind != MethodKind.Ordinary)
                continue;

            AddMethod(method, methodIds, includeFactorySignatures, methods, diagnostics);
        }

        return methods.ToImmutable();
    }

    private static void AddMethod(
        IMethodSymbol method,
        HashSet<string> methodIds,
        HashSet<string> includeFactorySignatures,
        ImmutableArray<QueryContextMethodModel>.Builder methods,
        ImmutableArray<QueryContextDiagnostic>.Builder diagnostics)
    {
        string methodId = MethodId(method);
        if (!ValidateMethod(method, methodId, methodIds, includeFactorySignatures, diagnostics))
            return;

        var parameters = ImmutableArray.CreateBuilder<QueryContextParameterModel>();
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (!TryCreateParameter(parameter, parameters.Count, out QueryContextParameterModel? model, out string message))
            {
                diagnostics.Add(new(QueryContextDiagnostics.UnsupportedDefaultValue, message));
                return;
            }

            parameters.Add(model);
        }

        methods.Add(new(
            method.Name,
            methodId,
            method.ReturnType.ToDisplayString(s_typeFormat),
            parameters.ToImmutable()));
    }

    private static bool ValidateMethod(
        IMethodSymbol method,
        string methodId,
        HashSet<string> methodIds,
        HashSet<string> includeFactorySignatures,
        ImmutableArray<QueryContextDiagnostic>.Builder diagnostics)
    {
        if (method.IsGenericMethod ||
            method.ReturnsVoid ||
            ContainsUnsupportedType(method.ReturnType) ||
            method.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None ||
                ContainsUnsupportedType(parameter.Type)))
        {
            diagnostics.Add(new(
                QueryContextDiagnostics.InvalidMethodShape,
                $"Query context method '{method.ToDisplayString()}' has an unsupported signature."));
            return false;
        }

        if (string.IsNullOrWhiteSpace(methodId))
        {
            diagnostics.Add(new(
                QueryContextDiagnostics.InvalidMethodShape,
                $"Query context method '{method.ToDisplayString()}' must declare a non-empty method id."));
            return false;
        }

        if (!methodIds.Add(methodId))
        {
            diagnostics.Add(new(
                QueryContextDiagnostics.DuplicateMethodId,
                $"Query context method id '{methodId}' is used more than once in '{method.ContainingType.ToDisplayString()}'."));
            return false;
        }

        if (!includeFactorySignatures.Add(IncludeFactorySignature(method)))
        {
            diagnostics.Add(new(
                QueryContextDiagnostics.InvalidMethodShape,
                $"Query context method '{method.ToDisplayString()}' produces a duplicate include factory signature."));
            return false;
        }

        return true;
    }

    private static bool TryCreateParameter(
        IParameterSymbol parameter,
        int index,
        out QueryContextParameterModel model,
        out string message)
    {
        string defaultValue = "null";
        if (parameter.HasExplicitDefaultValue &&
            !TryDefaultValueSource(parameter, out defaultValue))
        {
            model = null!;
            message = $"Parameter '{parameter.Name}' default value cannot be represented in a SiftQL descriptor.";
            return false;
        }

        model = new(
            string.IsNullOrWhiteSpace(parameter.Name) ? "arg" + index.ToString(CultureInfo.InvariantCulture) : parameter.Name,
            parameter.Type.ToDisplayString(s_typeFormat),
            parameter.HasExplicitDefaultValue,
            defaultValue);
        message = string.Empty;
        return true;
    }

    private static bool TryDefaultValueSource(IParameterSymbol parameter, out string source)
    {
        object? value = parameter.ExplicitDefaultValue;
        if (value is null)
        {
            source = "null";
            return true;
        }

        source = value switch
        {
            bool item => item ? "true" : "false",
            string item => StringLiteral(item),
            sbyte or byte or short or ushort or int => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            uint item => item.ToString(CultureInfo.InvariantCulture) + "u",
            long item => item.ToString(CultureInfo.InvariantCulture) + "L",
            ulong item => item.ToString(CultureInfo.InvariantCulture) + "UL",
            float item => FloatLiteral(item),
            double item => DoubleLiteral(item),
            decimal item => item.ToString(CultureInfo.InvariantCulture) + "m",
            _ => string.Empty,
        };
        return source.Length != 0;
    }

    private static bool ContainsUnsupportedType(ITypeSymbol type)
    {
        if (type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
            return true;
        if (type is IArrayTypeSymbol array)
            return ContainsUnsupportedType(array.ElementType);
        if (type is INamedTypeSymbol named)
            return named.TypeArguments.Any(ContainsUnsupportedType);
        return false;
    }

    private static string ContextId(GeneratorAttributeSyntaxContext context)
    {
        AttributeData? attribute = context.Attributes.FirstOrDefault(static attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), ContextAttributeName, StringComparison.Ordinal));
        return attribute?.ConstructorArguments.Length == 1
            ? attribute.ConstructorArguments[0].Value as string ?? string.Empty
            : string.Empty;
    }

    private static string MethodId(IMethodSymbol method)
    {
        AttributeData? attribute = method.GetAttributes().FirstOrDefault(static attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), MethodAttributeName, StringComparison.Ordinal));
        return attribute?.ConstructorArguments.Length == 1
            ? attribute.ConstructorArguments[0].Value as string ?? method.Name
            : method.Name;
    }

    private static string HelperName(string interfaceName)
    {
        string baseName = interfaceName.Length > 1 &&
            interfaceName[0] == 'I' &&
            char.IsUpper(interfaceName[1])
                ? interfaceName.Substring(1)
                : interfaceName;
        return baseName + "SiftQlExtensions";
    }

    private static string NamespaceName(INamedTypeSymbol type) =>
        type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();

    private static string AccessibilityText(INamedTypeSymbol type) =>
        type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

    private static string IncludeFactorySignature(IMethodSymbol method) =>
        method.Name + "|" + method.Parameters.Length.ToString(CultureInfo.InvariantCulture);

    private static string FloatLiteral(float value) =>
        float.IsNaN(value)
            ? "float.NaN"
            : float.IsPositiveInfinity(value)
                ? "float.PositiveInfinity"
                : float.IsNegativeInfinity(value)
                    ? "float.NegativeInfinity"
                    : value.ToString("R", CultureInfo.InvariantCulture) + "f";

    private static string DoubleLiteral(double value) =>
        double.IsNaN(value)
            ? "double.NaN"
            : double.IsPositiveInfinity(value)
                ? "double.PositiveInfinity"
                : double.IsNegativeInfinity(value)
                    ? "double.NegativeInfinity"
                    : value.ToString("R", CultureInfo.InvariantCulture) + "d";

    private static string StringLiteral(string value)
    {
        var source = new StringBuilder();
        CSharpStringLiteral.AppendTo(source, value);
        return source.ToString();
    }
}

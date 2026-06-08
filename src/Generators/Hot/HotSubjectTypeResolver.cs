using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SiftQL.Generators.Hot;

internal static class HotSubjectTypeResolver
{
    public static INamedTypeSymbol? Resolve(Compilation compilation, string subjectType) =>
        TryParse(subjectType, out SubjectTypeSpec spec)
            ? Resolve(compilation, spec)
            : null;

    private static INamedTypeSymbol? Resolve(Compilation compilation, SubjectTypeSpec spec)
    {
        INamedTypeSymbol? type = ResolveMetadataName(compilation, spec.MetadataName);
        if (type is null || spec.TypeArguments.Length == 0)
            return type;

        var arguments = new ITypeSymbol[spec.TypeArguments.Length];
        for (int i = 0; i < arguments.Length; i++)
        {
            INamedTypeSymbol? argument = Resolve(compilation, spec.TypeArguments[i]);
            if (argument is null)
                return null;
            arguments[i] = argument;
        }

        return type.Arity == arguments.Length ? type.Construct(arguments) : null;
    }

    private static INamedTypeSymbol? ResolveMetadataName(Compilation compilation, string metadataName) =>
        ResolveSpecialType(compilation, metadataName) ??
        compilation.GetTypeByMetadataName(metadataName) ??
        compilation.GetTypeByMetadataName(metadataName.Replace('+', '.'));

    private static INamedTypeSymbol? ResolveSpecialType(Compilation compilation, string metadataName)
    {
        Microsoft.CodeAnalysis.SpecialType specialType = metadataName switch
        {
            "System.Boolean" => Microsoft.CodeAnalysis.SpecialType.System_Boolean,
            "System.Byte" => Microsoft.CodeAnalysis.SpecialType.System_Byte,
            "System.Char" => Microsoft.CodeAnalysis.SpecialType.System_Char,
            "System.Decimal" => Microsoft.CodeAnalysis.SpecialType.System_Decimal,
            "System.Double" => Microsoft.CodeAnalysis.SpecialType.System_Double,
            "System.Int16" => Microsoft.CodeAnalysis.SpecialType.System_Int16,
            "System.Int32" => Microsoft.CodeAnalysis.SpecialType.System_Int32,
            "System.Int64" => Microsoft.CodeAnalysis.SpecialType.System_Int64,
            "System.Object" => Microsoft.CodeAnalysis.SpecialType.System_Object,
            "System.SByte" => Microsoft.CodeAnalysis.SpecialType.System_SByte,
            "System.Single" => Microsoft.CodeAnalysis.SpecialType.System_Single,
            "System.String" => Microsoft.CodeAnalysis.SpecialType.System_String,
            "System.UInt16" => Microsoft.CodeAnalysis.SpecialType.System_UInt16,
            "System.UInt32" => Microsoft.CodeAnalysis.SpecialType.System_UInt32,
            "System.UInt64" => Microsoft.CodeAnalysis.SpecialType.System_UInt64,
            _ => Microsoft.CodeAnalysis.SpecialType.None,
        };
        if (specialType == Microsoft.CodeAnalysis.SpecialType.None)
            return null;

        INamedTypeSymbol symbol = compilation.GetSpecialType(specialType);
        return symbol.TypeKind == TypeKind.Error ? null : symbol;
    }

    private static bool TryParse(string text, out SubjectTypeSpec spec)
    {
        string typeName = TopLevelTypeName(text.Trim());
        int bracket = typeName.IndexOf('[');
        if (bracket < 0)
        {
            spec = new SubjectTypeSpec(typeName, ImmutableArray<SubjectTypeSpec>.Empty);
            return !string.IsNullOrWhiteSpace(spec.MetadataName);
        }

        string metadataName = typeName.Substring(0, bracket).Trim();
        var arguments = ImmutableArray.CreateBuilder<SubjectTypeSpec>();
        if (string.IsNullOrWhiteSpace(metadataName) ||
            !TryParseArguments(typeName, bracket, arguments))
        {
            spec = default;
            return false;
        }

        spec = new SubjectTypeSpec(metadataName, arguments.ToImmutable());
        return true;
    }

    private static string TopLevelTypeName(string text)
    {
        int comma = TopLevelComma(text);
        return comma < 0 ? text : text.Substring(0, comma).Trim();
    }

    private static int TopLevelComma(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            depth += text[i] switch
            {
                '[' => 1,
                ']' => -1,
                _ => 0,
            };
            if (text[i] == ',' && depth == 0)
                return i;
        }

        return -1;
    }

    private static bool TryParseArguments(
        string typeName,
        int start,
        ImmutableArray<SubjectTypeSpec>.Builder arguments)
    {
        int outerEnd = typeName.Length - 1;
        if (start >= outerEnd || typeName[start] != '[' || typeName[outerEnd] != ']')
            return false;

        int index = start + 1;
        while (index < outerEnd)
        {
            SkipSeparators(typeName, outerEnd, ref index);
            if (index >= outerEnd)
                break;

            int argumentStart = index;
            int argumentEnd = typeName[index] == '['
                ? MatchingBracket(typeName, index)
                : ArgumentEnd(typeName, index, outerEnd);
            if (argumentEnd < 0 || argumentEnd > outerEnd)
                return false;

            string argumentText = typeName[index] == '['
                ? typeName.Substring(argumentStart + 1, argumentEnd - argumentStart - 1)
                : typeName.Substring(argumentStart, argumentEnd - argumentStart);
            if (!TryParse(argumentText, out SubjectTypeSpec argument))
                return false;

            arguments.Add(argument);
            index = argumentEnd + (typeName[argumentStart] == '[' ? 1 : 0);
        }

        SkipSeparators(typeName, outerEnd, ref index);
        return index == outerEnd && arguments.Count > 0;
    }

    private static void SkipSeparators(string text, int end, ref int index)
    {
        while (index < end && (char.IsWhiteSpace(text[index]) || text[index] == ','))
            index++;
    }

    private static int MatchingBracket(string text, int start)
    {
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '[')
                depth++;
            if (text[i] == ']')
                depth--;
            if (depth == 0)
                return i;
        }

        return -1;
    }

    private static int ArgumentEnd(string text, int start, int outerEnd)
    {
        int depth = 0;
        for (int i = start; i < outerEnd; i++)
        {
            if (text[i] == '[')
                depth++;
            if (text[i] == ']')
                depth--;
            if (text[i] == ',' && depth == 0)
                return i;
        }

        return outerEnd;
    }

    private readonly record struct SubjectTypeSpec(
        string MetadataName,
        ImmutableArray<SubjectTypeSpec> TypeArguments);
}

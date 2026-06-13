using System.Text;

namespace SiftQL.Generators.Hot;

internal static class HotProviderRuntimeEmitter
{
    public static void EmitHelpers(StringBuilder source)
    {
        source.AppendLine("    private static FilterField RequireField(Type subjectType, string fieldName) =>");
        source.AppendLine("        subjectType == typeof(ProjectedEvent)");
        source.AppendLine("            ? ProjectedEventFilterSchema.CreateField(fieldName)");
        source.AppendLine("            : FilterSchema.For(subjectType).TryGetField(fieldName, out var field)");
        source.AppendLine("            ? field");
        source.AppendLine("            : throw new InvalidOperationException($\"Hot provider field '{fieldName}' is unavailable for {subjectType.FullName}.\");");
        source.AppendLine();
        source.AppendLine("    private static ProjectedEventValue Project(FilterField field, object subject) =>");
        source.AppendLine("        field.ProjectionAccessor is null");
        source.AppendLine("            ? ProjectedEventValue.FromScalar(field.Getter(subject))");
        source.AppendLine("            : field.ProjectionAccessor(subject);");
        source.AppendLine();
        source.AppendLine("    private static FilterValue Parameter(IReadOnlyList<FilterValue> parameters, int index) =>");
        source.AppendLine("        parameters[index];");
        source.AppendLine();
    }

    public static void EmitRegistration(StringBuilder source, string providerName, string manifestHash)
    {
        source.AppendLine("internal static partial class " + providerName + "Registration");
        source.AppendLine("{");
        source.AppendLine("    [ModuleInitializer]");
        source.Append("    internal static void Register() => HotProviderRegistrationContext.RegisterFactory(static () => new ");
        source.Append(providerName).Append("(), ");
        AppendLiteral(source, manifestHash);
        source.AppendLine(");");
        source.AppendLine("}");
    }

    private static void AppendLiteral(StringBuilder source, string value)
    {
        CSharpStringLiteral.AppendTo(source, value);
    }
}

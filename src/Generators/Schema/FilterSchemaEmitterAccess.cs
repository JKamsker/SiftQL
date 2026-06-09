using System.Text;

namespace SiftQL.Generators.Schema;

internal static class FilterSchemaEmitterAccess
{
    public static string AccessExpression(GeneratedSchema schema, GeneratedField field)
    {
        if (field.UsesCollectionAccessor)
        {
            var source = new StringBuilder("FilterCollectionFieldValues.Read(subject, ");
            AppendLiteral(source, field.Access);
            return source.Append(')').ToString();
        }

        return field.SafeAccess.StartsWith("((", StringComparison.Ordinal)
            ? field.SafeAccess
            : "((" + schema.TypeName + ")subject)." + field.SafeAccess;
    }

    public static void AppendFieldAccess(StringBuilder source, GeneratedField field)
    {
        if (field.UsesCollectionAccessor)
        {
            source.Append("null");
            return;
        }

        source.Append("FilterFieldAccess.ForProperty(");
        AppendLiteral(source, field.Access);
        source.Append(')');
    }

    private static void AppendLiteral(StringBuilder source, string value)
    {
        CSharpStringLiteral.AppendTo(source, value);
    }
}

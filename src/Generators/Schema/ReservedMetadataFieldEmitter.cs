using System.Text;

namespace SiftQL.Generators.Schema;

internal static class ReservedMetadataFieldEmitter
{
    public static void EmitEventTypeField(
        StringBuilder source,
        string typeName,
        bool isSealed,
        bool allowsProjectionAccessor) =>
        EmitVirtualField(
            source,
            "subjectType",
            "typeof(" + typeName + ").FullName ?? typeof(" + typeName + ").Name",
            "subject.GetType().FullName ?? subject.GetType().Name",
            isSealed,
            allowsProjectionAccessor);

    public static void EmitEventNameField(
        StringBuilder source,
        string typeName,
        bool isSealed,
        bool allowsProjectionAccessor) =>
        EmitVirtualField(
            source,
            "subjectName",
            "typeof(" + typeName + ").Name",
            "subject.GetType().Name",
            isSealed,
            allowsProjectionAccessor);

    private static void EmitVirtualField(
        StringBuilder source,
        string name,
        string constantExpression,
        string dynamicExpression,
        bool isSealed,
        bool allowsProjectionAccessor)
    {
        string valueExpression = isSealed ? constantExpression : dynamicExpression;
        string accessExpression = isSealed ? "FilterFieldAccess.ForConstant(" + constantExpression + ")" : "null";
        string projectionAccessor = allowsProjectionAccessor
            ? "static subject => ProjectionValueFactory.FromString(" + valueExpression + ")"
            : "null";
        source.Append("            new(");
        CSharpStringLiteral.AppendTo(source, name);
        source.Append(", typeof(string), FilterFieldKind.Scalar, static subject => ");
        source.Append(valueExpression);
        source.Append(", new FilterScalarAccessor(FilterScalarKind.String, text: static subject => ");
        source.Append(valueExpression);
        source.Append("), null, ");
        source.Append(projectionAccessor);
        source.Append(", ");
        source.Append(accessExpression);
        source.AppendLine("),");
    }
}

using System.Linq.Expressions;
using System.Reflection;

namespace SiftQL.Translation;

// Shared path-segment format for subtype-projected member reads. A downcast
// member read `(x.Member as Sub).Prop`, where `Prop` is declared only on the
// subtype, lowers to the field path `Member.<Sub>.Prop`. The angle-bracketed
// segment cannot collide with a real C# member name, so each subtype keeps its
// extra members in a distinct field namespace. The translator emits the segment
// and the schema builder expands registered value-object subtypes under it; both
// sides call Segment() so the format never drifts. See [[SubjectTypeMetadata]].
internal static class SubtypeProjection
{
    public static string Segment(Type subtype) => "<" + subtype.Name + ">";

    // Recognizes `(operand as Sub)` / `((Sub)operand)` wrapping a member access
    // where the accessed member is genuinely subtype-specific (not reachable
    // through the operand's declared type). Base-declared members stay flat so
    // existing cast-then-base-member translation is unchanged.
    public static bool TryResolveSubtypeMember(
        Expression? container,
        MemberInfo member,
        out Type subtype)
    {
        subtype = null!;
        if (container is not UnaryExpression
            {
                NodeType: ExpressionType.TypeAs or ExpressionType.Convert or ExpressionType.ConvertChecked,
            } cast)
        {
            return false;
        }

        Type target = cast.Type;
        Type operandType = cast.Operand.Type;
        // Reference downcast only: target is a strict subtype of the operand's
        // declared type. Upcasts, identity, and numeric/enum conversions stay flat.
        if (target == operandType || target.IsValueType || !operandType.IsAssignableFrom(target))
            return false;

        // Member already reachable through the declared base type -> flat path.
        if (member.DeclaringType is { } declaring && declaring.IsAssignableFrom(operandType))
            return false;

        subtype = target;
        return true;
    }
}

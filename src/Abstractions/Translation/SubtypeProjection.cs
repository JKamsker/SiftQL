using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SiftQL.Translation;

// Shared path-segment format for subtype-projected member reads. A downcast
// member read `(x.Member as Sub).Prop`, where `Prop` is declared only on the
// subtype, lowers to a field path under an angle-bracketed subtype segment. The
// segment includes a short hash of the type identity so same-named subtypes keep
// distinct field namespaces without introducing '.' into the dot-separated path.
// The translator emits the segment and the schema builder expands registered
// value-object subtypes under it; both sides call Segment() so the format never
// drifts. See [[SubjectTypeMetadata]].
internal static class SubtypeProjection
{
    private static readonly ConcurrentDictionary<Type, string> s_segments = new();

    public static string Segment(Type subtype) =>
        s_segments.GetOrAdd(subtype, CreateSegment);

    private static string CreateSegment(Type subtype)
    {
        string identity = subtype.AssemblyQualifiedName ?? subtype.FullName ?? subtype.Name;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        return "<" + subtype.Name + "#" + hash + ">";
    }

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

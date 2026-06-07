using SiftQL;
using SiftQL.Expressions;

namespace SiftQL.Hot;

public interface ITieredHotManifestSink
{
    void RecordHotFilter(
        Type subjectType,
        FilterExpression expression,
        long evaluations,
        long matches);

    void RecordHotProjection(
        Type subjectType,
        EventProjectionExpression projection,
        long materializations,
        long payloadWrites);
}

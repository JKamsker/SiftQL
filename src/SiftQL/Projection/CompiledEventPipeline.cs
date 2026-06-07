using SiftQL;
using SiftQL.Expressions;
using SiftQL.Kernel;
using SiftQL.Projected;
using MessagePack;

namespace SiftQL.Projection;

public sealed class CompiledEventPipeline<TContext>
{
    private readonly PipelineStage<TContext>[] _stages;

    internal CompiledEventPipeline(
        string key,
        FilterExpression indexFilter,
        IReadOnlyList<PipelineStage<TContext>> stages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        IndexFilter = indexFilter ?? throw new ArgumentNullException(nameof(indexFilter));
        _stages = stages.ToArray();
    }

    public string Key { get; }
    public FilterExpression IndexFilter { get; }

    public async ValueTask<ProjectedEvent?> ProjectAsync(
        object subject,
        TContext context,
        CancellationToken cancellationToken)
    {
        object current = subject ?? throw new ArgumentNullException(nameof(subject));
        for (int i = 0; i < _stages.Length; i++)
        {
            object? next = await _stages[i]
                .ApplyAsync(current, context, cancellationToken)
                .ConfigureAwait(false);
            if (next is null)
                return null;
            current = next;
        }

        return current as ProjectedEvent;
    }

    public async ValueTask<ReadOnlyMemory<byte>?> ProjectPayloadAsync(
        object subject,
        TContext context,
        MessagePackSerializerOptions options,
        CancellationToken cancellationToken)
    {
        object current = subject ?? throw new ArgumentNullException(nameof(subject));
        for (int i = 0; i < _stages.Length; i++)
        {
            if (i == _stages.Length - 1 && _stages[i].CanWritePayload)
            {
                return await _stages[i]
                    .ApplyPayloadAsync(current, context, options, cancellationToken)
                    .ConfigureAwait(false);
            }

            object? next = await _stages[i]
                .ApplyAsync(current, context, cancellationToken)
                .ConfigureAwait(false);
            if (next is null)
                return null;
            current = next;
        }

        return current is ProjectedEvent projected
            ? MessagePackSerializer.Serialize(projected, options)
            : null;
    }
}

internal abstract class PipelineStage<TContext>
{
    public abstract ValueTask<object?> ApplyAsync(
        object subject,
        TContext context,
        CancellationToken cancellationToken);

    public virtual ValueTask<ReadOnlyMemory<byte>> ApplyPayloadAsync(
        object subject,
        TContext context,
        MessagePackSerializerOptions options,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual bool CanWritePayload => false;
}

internal sealed class PipelineFilterStage<TContext>(CompiledKernel kernel) : PipelineStage<TContext>
{
    public override ValueTask<object?> ApplyAsync(
        object subject,
        TContext context,
        CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;
        return new ValueTask<object?>(kernel.Matches(subject) ? subject : null);
    }
}

internal sealed class PipelineProjectionStage<TContext>(
    CompiledProjection<TContext> projection) : PipelineStage<TContext>
{
    public override async ValueTask<object?> ApplyAsync(
        object subject,
        TContext context,
        CancellationToken cancellationToken) =>
        await projection.ProjectAsync(subject, context, cancellationToken).ConfigureAwait(false);

    public override ValueTask<ReadOnlyMemory<byte>> ApplyPayloadAsync(
        object subject,
        TContext context,
        MessagePackSerializerOptions options,
        CancellationToken cancellationToken) =>
        projection.ProjectPayloadAsync(subject, context, options, cancellationToken);

    public override bool CanWritePayload => true;
}

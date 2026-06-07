namespace SiftQL.Hot;

public interface IRuntimeHotProviderBatchQueue
{
    void Queue(RuntimeHotProviderBatch batch);
}

public interface IRuntimeHotProviderBatchCompiler
{
    ValueTask<RuntimeHotProviderBatchCompileResult> CompileAsync(
        RuntimeHotProviderBatch batch,
        CancellationToken cancellationToken = default);
}

namespace SiftQL.Hot;

internal static class HotCompilationManifestFileOps
{
    public static void MoveTempIntoPlace(string temp, string path)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (
                attempt < 9 &&
                (ex is IOException || ex is UnauthorizedAccessException))
            {
                Thread.Sleep(10);
            }
        }

        File.Move(temp, path, overwrite: true);
    }

    public static void TryDeleteTemp(string temp)
    {
        try
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
        catch
        {
            // Best-effort cleanup for temp files left by interrupted writes.
        }
    }

    public static void ValidateOptions(HotCompilationManifestWriterOptions options)
    {
        if (options.MaxEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MaxEntries));
        if (options.Retention <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.Retention));
        if (options.CoalesceDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.CoalesceDelay));
    }
}

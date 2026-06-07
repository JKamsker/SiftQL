using System.Diagnostics;

namespace SiftQL.Examples.ShaRpc.Server.Transport;

public sealed class ClientProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _stdout;
    private readonly Task _stderr;

    private ClientProcess(Process process)
    {
        _process = process;
        _stdout = PumpAsync(process.StandardOutput, "[client] ");
        _stderr = PumpAsync(process.StandardError, "[client:err] ");
    }

    public static ClientProcess Start(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(ClientAssemblyPath());
        startInfo.ArgumentList.Add(pipeName);

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The client process could not be started.");
        return new ClientProcess(process);
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(_stdout, _stderr).ConfigureAwait(false);
        if (_process.ExitCode != 0)
            throw new InvalidOperationException($"Client process exited with code {_process.ExitCode}.");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);

        await Task.WhenAll(_stdout, _stderr).ConfigureAwait(false);
        _process.Dispose();
    }

    private static async Task PumpAsync(TextReader reader, string prefix)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            Console.WriteLine(prefix + line);
    }

    private static string ClientAssemblyPath()
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string targetFramework = new DirectoryInfo(baseDirectory).Name;
        string configuration = Directory.GetParent(baseDirectory)?.Name
            ?? throw new InvalidOperationException("Cannot determine build configuration.");
        string examplesDirectory = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", ".."));
        string path = Path.Combine(
            examplesDirectory,
            "Client",
            "bin",
            configuration,
            targetFramework,
            "Client.dll");
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException("Build the ShaRPC client example before running the server.", path);
    }
}

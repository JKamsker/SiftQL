using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterPackageAnalyzerAssetsTests
{
    [Fact]
    public void FiltersPackageIncludesGeneratorAnalyzerAssets()
    {
        string root = FindRepoRoot();
        string output = Path.Combine(
            Path.GetTempPath(),
            "SiftQLPack",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            RunDotnet(
                root,
                "pack",
                "src/SiftQL/SiftQL.csproj",
                "-c",
                "Release",
                "-o",
                output);

            string packagePath = Directory
                .EnumerateFiles(output, "SiftQL.*.nupkg")
                .Single();
            string[] entries = PackageEntries(packagePath);

            Assert.Contains(
                "analyzers/dotnet/cs/SiftQL.Generators.dll",
                entries);
            Assert.Contains("analyzers/dotnet/cs/System.Text.Json.dll", entries);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static string[] PackageEntries(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries
            .Select(static entry => entry.FullName.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static void RunDotnet(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("dotnet did not start.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            "dotnet " + string.Join(" ", arguments) + " failed:" + Environment.NewLine + output + error);
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SiftQL.slnx")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "SiftQL", "SiftQL.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test source path.");
    }
}

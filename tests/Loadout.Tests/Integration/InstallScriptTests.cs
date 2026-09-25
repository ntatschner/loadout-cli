using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Skips on Windows, where <c>install.sh</c> is never run: Windows installs
/// from the MSI or the zip, and the Git Bash that happens to be on a developer's
/// PATH mangles drive-letter paths in ways no user of the script meets.
/// </summary>
public sealed class ShellFactAttribute : FactAttribute
{
    public ShellFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix-only: install.sh is the Linux and macOS installer.";
        }
    }
}

/// <summary>
/// What <c>build/install.sh</c> leaves behind, run against an archive shaped
/// like the ones the release publishes.
/// </summary>
/// <remarks>
/// The script installed the executable and nothing else, so the native library
/// shipped beside it in every Linux and macOS archive never reached the
/// machine. The Homebrew formula had been written to avoid exactly that; the
/// script it was copied from had not.
/// </remarks>
public sealed class InstallScriptTests : IDisposable
{
    private readonly string _root;

    public InstallScriptTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    [ShellFact]
    public async Task Native_libraries_are_installed_beside_the_binary()
    {
        var archive = WriteArchive(
            "loadout",
            "libonigwrap.dylib",
            "libonigwrap.so",
            "README.md",
            "LICENSE");

        var prefix = Path.Combine(_root, "prefix");

        var run = await RunAsync("--archive", archive, "--prefix", prefix);

        run.ExitCode.Should().Be(0, run.Output);

        var bin = Path.Combine(prefix, "bin");

        File.Exists(Path.Combine(bin, "loadout")).Should().BeTrue();

        // The runtime looks for these in the executable's own directory, so
        // anywhere else is as good as not installing them.
        File.Exists(Path.Combine(bin, "libonigwrap.dylib")).Should().BeTrue(
            "the macOS archive carries it beside the binary and the binary loads it from there");
        File.Exists(Path.Combine(bin, "libonigwrap.so")).Should().BeTrue(
            "the Linux archive carries it beside the binary and the binary loads it from there");

        // Documentation is not a command and does not belong on PATH.
        File.Exists(Path.Combine(bin, "README.md")).Should().BeFalse();
        File.Exists(Path.Combine(bin, "LICENSE")).Should().BeFalse();
    }

    /// <summary>A tar.gz whose entries sit at its root, as the release packs them.</summary>
    private string WriteArchive(params string[] names)
    {
        var path = Path.Combine(_root, "loadout-9.9.9-test.tar.gz");

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        using var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);

        foreach (var name in names)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "./" + name)
            {
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"pretend {name}\n")),
            };

            tar.WriteEntry(entry);
        }

        return path;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    {
        var script = Path.Combine(RepositoryRoot(), "build", "install.sh");

        File.Exists(script).Should().BeTrue($"the release ships {script}");

        var start = new ProcessStartInfo("sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add(script);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, await output + await error);
    }

    /// <summary>Walks up from the test binary until the repository is found.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "build")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}

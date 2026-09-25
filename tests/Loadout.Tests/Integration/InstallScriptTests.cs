using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Skips on Windows, where <c>install.sh</c> is never run: Windows installs
/// with <c>install.ps1</c>, the MSI or the zip, and the Git Bash that happens
/// to be on a developer's PATH mangles drive-letter paths in ways no user of
/// the script meets.
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
/// Skips off Windows, where <c>install.ps1</c> refuses before it does anything
/// worth testing: it verifies an MSI's Authenticode signature and runs msiexec.
/// </summary>
public sealed class InstallerFactAttribute : FactAttribute
{
    public InstallerFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only: install.ps1 is the Windows installer.";
        }
    }
}

/// <summary>
/// What the one-command installers do, run against a mirror of a release laid
/// out as the release publishes it: a feed naming the version, the archives,
/// and <c>SHA256SUMS</c>.
/// </summary>
/// <remarks>
/// <para>
/// A mirror in a local directory stands in for GitHub, which both scripts
/// accept because a mirror on a file share is a real way to install. The
/// scripts' own download code is therefore not what these cover; that was run
/// by hand against the published release.
/// </para>
/// <para>
/// The Windows tests stop before msiexec on purpose, with <c>-WhatIf</c> on top
/// of an MSI that cannot pass the signature check, so a regression that let one
/// through would still not install anything on the machine running the suite.
/// Nothing here can prove the success path of <c>install.ps1</c> without a
/// signed MSI, and it does not pretend to.
/// </para>
/// </remarks>
public sealed class InstallScriptTests : IDisposable
{
    private const string Version = "9.9.9";

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
        // The script installed the executable and nothing else, so the native
        // library shipped beside it in every Linux and macOS archive never
        // reached the machine.
        var archive = WriteArchive(
            Path.Combine(_root, "loadout-9.9.9-test.tar.gz"),
            "loadout",
            "libonigwrap.dylib",
            "libonigwrap.so",
            "README.md",
            "LICENSE");

        var prefix = Path.Combine(_root, "prefix");

        var run = await ShellAsync("--archive", archive, "--prefix", prefix);

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

    [ShellFact]
    public async Task It_installs_this_platforms_archive_from_a_release()
    {
        var mirror = Mirror($"loadout-{Version}-{UnixRid()}.tar.gz", corruptAfterHashing: false);

        var prefix = Path.Combine(_root, "prefix");

        var run = await ShellAsync("--base-url", mirror, "--prefix", prefix);

        run.ExitCode.Should().Be(0, run.Output);
        run.Output.Should().Contain("Checksum verified.");

        // The version came from the feed, which is how "latest" works: the
        // archive name carries it and nothing else says what it is.
        run.Output.Should().Contain($"loadout {Version} for {UnixRid()}");

        File.Exists(Path.Combine(prefix, "bin", "loadout")).Should().BeTrue();
    }

    [ShellFact]
    public async Task A_download_that_fails_its_checksum_is_not_installed()
    {
        var mirror = Mirror($"loadout-{Version}-{UnixRid()}.tar.gz", corruptAfterHashing: true);

        var prefix = Path.Combine(_root, "prefix");

        var run = await ShellAsync("--base-url", mirror, "--prefix", prefix);

        run.ExitCode.Should().NotBe(0, run.Output);
        run.Output.Should().Contain("checksum mismatch");
        Directory.Exists(Path.Combine(prefix, "bin")).Should().BeFalse("nothing is installed from a download that failed its check");
    }

    [ShellFact]
    public async Task A_download_the_manifest_does_not_list_is_not_installed()
    {
        var mirror = Mirror($"loadout-{Version}-{UnixRid()}.tar.gz", corruptAfterHashing: false);

        // A manifest for other files must not vouch for this one.
        File.WriteAllText(
            Path.Combine(mirror, "SHA256SUMS"),
            $"{new string('0', 64)}  loadout-{Version}-some-other-platform.tar.gz\n");

        var prefix = Path.Combine(_root, "prefix");

        var run = await ShellAsync("--base-url", mirror, "--prefix", prefix);

        run.ExitCode.Should().NotBe(0, run.Output);
        run.Output.Should().Contain("doesn't list");
        Directory.Exists(Path.Combine(prefix, "bin")).Should().BeFalse();
    }

    [ShellFact]
    public async Task A_dry_run_verifies_and_installs_nothing()
    {
        var mirror = Mirror($"loadout-{Version}-{UnixRid()}.tar.gz", corruptAfterHashing: false);

        var prefix = Path.Combine(_root, "prefix");

        var run = await ShellAsync("--base-url", mirror, "--prefix", prefix, "--dry-run");

        run.ExitCode.Should().Be(0, run.Output);
        run.Output.Should().Contain("Checksum verified.");
        Directory.Exists(prefix).Should().BeFalse("a dry run changes nothing");
    }

    [InstallerFact]
    public async Task The_Windows_installer_refuses_an_msi_that_fails_its_checksum()
    {
        var mirror = Mirror($"loadout-{Version}-{WindowsRid()}.msi", corruptAfterHashing: true);

        var run = await PowerShellAsync(mirror);

        run.ExitCode.Should().NotBe(0, run.Output);
        run.Output.Should().Contain("Checksum mismatch");
        run.Output.Should().NotContain("What if", "it refused before getting anywhere near msiexec");
    }

    [InstallerFact]
    public async Task The_Windows_installer_refuses_an_unsigned_msi_even_when_the_checksum_matches()
    {
        // The checksum comes from the same place as the file, so it proves the
        // download intact, not who built it. Only the signature says that.
        var mirror = Mirror($"loadout-{Version}-{WindowsRid()}.msi", corruptAfterHashing: false);

        var run = await PowerShellAsync(mirror);

        run.ExitCode.Should().NotBe(0, run.Output);
        run.Output.Should().Contain("Checksum verified.");
        run.Output.Should().Contain("isn't validly signed");
        run.Output.Should().NotContain("What if");
    }

    /// <summary>
    /// A directory laid out as a release is: feed.json, the named asset, and a
    /// SHA256SUMS listing it. The asset is a release-shaped tar.gz, or opaque
    /// bytes for an MSI.
    /// </summary>
    private string Mirror(string asset, bool corruptAfterHashing)
    {
        var mirror = Path.Combine(_root, "mirror");
        Directory.CreateDirectory(mirror);

        File.WriteAllText(
            Path.Combine(mirror, "feed.json"),
            $$"""
            {
              "schemaVersion": 1,
              "version": "{{Version}}",
              "artifacts": {}
            }
            """);

        var path = Path.Combine(mirror, asset);

        if (asset.EndsWith(".tar.gz", StringComparison.Ordinal))
        {
            WriteArchive(path, "loadout", "README.md");
        }
        else
        {
            File.WriteAllText(path, "pretend installer");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        File.WriteAllText(Path.Combine(mirror, "SHA256SUMS"), $"{hash}  {asset}\n");

        if (corruptAfterHashing)
        {
            File.AppendAllText(path, "tampered");
        }

        return mirror;
    }

    /// <summary>A tar.gz whose entries sit at its root, as the release packs them.</summary>
    private static string WriteArchive(string path, params string[] names)
    {
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

    private static string Architecture() =>
        RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";

    private static string UnixRid() => (OperatingSystem.IsMacOS() ? "osx-" : "linux-") + Architecture();

    private static string WindowsRid() => "win-" + Architecture();

    private static Task<(int ExitCode, string Output)> ShellAsync(params string[] arguments) =>
        RunAsync("sh", [Script("install.sh"), .. arguments]);

    /// <summary>
    /// Windows PowerShell 5.1, because that is what a fresh machine opens and
    /// what `irm | iex` runs in. It is also where the traps are.
    /// </summary>
    private static Task<(int ExitCode, string Output)> PowerShellAsync(string mirror) =>
        RunAsync("powershell.exe",
        [
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Script("install.ps1"),
            "-BaseUrl", mirror,
            "-WhatIf",
        ]);

    private static string Script(string name)
    {
        var script = Path.Combine(RepositoryRoot(), "build", name);

        File.Exists(script).Should().BeTrue($"the release publishes {script}");

        return script;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

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

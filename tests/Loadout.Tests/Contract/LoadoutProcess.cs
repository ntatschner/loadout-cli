using System.Diagnostics;
using System.Text.Json;

namespace Loadout.Tests.Contract;

/// <summary>What one run of the built command line produced.</summary>
/// <param name="ExitCode">Its exit code, which is itself a public contract.</param>
/// <param name="StandardOutput">Everything it wrote to stdout.</param>
/// <param name="StandardError">Everything it wrote to stderr.</param>
public sealed record LoadoutRun(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// The output parsed as JSON.
    /// </summary>
    /// <remarks>
    /// Fails loudly rather than returning null, and quotes what was actually
    /// written. A command that printed a Spectre-formatted table because
    /// <c>--json</c> was not honoured produces a parse error whose message
    /// would otherwise say nothing about the cause.
    /// </remarks>
    public JsonElement Json()
    {
        if (string.IsNullOrWhiteSpace(StandardOutput))
        {
            throw new InvalidOperationException(
                $"Nothing was written to standard output. Exit code {ExitCode}. "
                + $"Standard error: {StandardError}");
        }

        try
        {
            using var document = JsonDocument.Parse(StandardOutput);

            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Output was not JSON: {exception.Message}"
                + Environment.NewLine
                + StandardOutput[..Math.Min(600, StandardOutput.Length)],
                exception);
        }
    }
}

/// <summary>
/// Runs the built <c>loadout</c> against a throwaway home directory.
/// <para>
/// The command line is run as a real process rather than by calling into its
/// types, because the thing under test is what a script receives: the JSON is
/// written straight to standard output by <c>CommandOutput.WriteJson</c>, and
/// the exit code is the process's. Constructing a command object and inspecting
/// its return value would test neither.
/// </para>
/// <para>
/// Every run gets its own configuration, state and cache directories, pointed
/// at by environment variables. Without that these tests would read whichever
/// projects the developer happens to have registered, and would pass or fail
/// according to somebody's machine rather than the code.
/// </para>
/// </summary>
public sealed class LoadoutProcess : IDisposable
{
    private readonly string _home;

    /// <summary>Where this run's throwaway home lives.</summary>
    public string Home => _home;

    public LoadoutProcess()
    {
        _home = Path.Combine(
            Path.GetTempPath(),
            "loadout-contract-" + Guid.NewGuid().ToString("N")[..12]);

        Directory.CreateDirectory(_home);
    }

    /// <summary>
    /// The built command line, or null when it has not been built.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found by walking up from the test output to the repository and then down
    /// into the command line's own build. The copy that lands beside the tests
    /// cannot be used: the command line is published self-contained, so its
    /// runtimeconfig declares an included framework and
    /// <c>dotnet loadout.dll</c> fails with "Failed to run as a self-contained
    /// app" — the runtime it names is not there.
    /// </para>
    /// <para>
    /// Null rather than throwing, so a run that has not built the command line
    /// skips these tests with a reason instead of failing with a path.
    /// </para>
    /// </remarks>
    internal static string? Executable
    {
        get
        {
            var configuration = AppContext.BaseDirectory.Contains(
                Path.Combine("bin", "Release"), StringComparison.OrdinalIgnoreCase)
                ? "Release"
                : "Debug";

            var name = OperatingSystem.IsWindows() ? "loadout.exe" : "loadout";

            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                var cli = Path.Combine(
                    directory.FullName, "src", "Loadout.Cli", "bin", configuration);

                if (!Directory.Exists(cli))
                {
                    continue;
                }

                // A build can leave more than one runtime identifier behind —
                // cross-compiling win-arm64 alongside win-x64 is ordinary — and
                // taking whichever the filesystem happened to return first
                // would sometimes hand back an executable this machine cannot
                // run. The one matching this process is preferred, and the
                // search only falls back when there is nothing better.
                var candidates = Directory
                    .EnumerateFiles(cli, name, SearchOption.AllDirectories)
                    .ToList();

                // Matching the runtime identifier is not enough on its own.
                // Publishing leaves a RID directory behind whose assemblies
                // differ from a plain build even when the source is identical,
                // so once anybody has built an installer locally that copy is
                // the one picked, EnsureCurrent compares it with the assembly
                // beside these tests, finds two files that are not the same and
                // declares the tree stale. It was never stale: it was a
                // different build of the same code, and the whole suite failed
                // on it twice in one afternoon.
                //
                // Choosing among the copies that match what these tests were
                // compiled against makes the two agree by construction, and
                // leaves the guard to catch what it was written for — a tree
                // that genuinely has not been rebuilt, where nothing matches.
                var mine = Path.Combine(AppContext.BaseDirectory, "loadout.dll");

                var sameCode = File.Exists(mine)
                    ? candidates.Where(path =>
                        {
                            var theirs = Path.Combine(
                                Path.GetDirectoryName(path)!, "loadout.dll");

                            return File.Exists(theirs) && Same(mine, theirs);
                        }).ToList()
                    : candidates;

                // Falling back to everything when none match keeps the failure
                // the guard's to report, with the path it looked at, rather
                // than becoming "no command line found" here.
                var preferred = sameCode.Count > 0 ? sameCode : candidates;

                var current = System.Runtime.InteropServices.RuntimeInformation
                    .RuntimeIdentifier;

                var matching = preferred.FirstOrDefault(path =>
                    Path.GetFileName(Path.GetDirectoryName(path))
                        is { } rid && string.Equals(rid, current, StringComparison.OrdinalIgnoreCase));

                var found = matching ?? preferred.FirstOrDefault();

                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Windows' status code for a process that could not finish initialising.
    /// </summary>
    /// <remarks>
    /// STATUS_DLL_INIT_FAILED. Starting many processes in quick succession
    /// exhausts a per-session resource and the next one dies before its entry
    /// point runs: Process.Start succeeds, then the process exits with this and
    /// writes nothing at all. It is not a failure of the command, and it is
    /// distinguishable from one — a command that ran and failed says something.
    /// </remarks>
    private const int ProcessInitialisationFailed = unchecked((int)0xC0000142);

    /// <summary>
    /// How many of these may be starting at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0xC0000142 is Windows saying a process could not finish initialising,
    /// and it is what comes back when too many are asked for at the same
    /// moment. The retry below was written for a momentary version of that —
    /// an executable being scanned just after a build — and it holds for one.
    /// It does not hold for this: a run of the full suite failed thirty-nine
    /// contract tests together, every one of them having exhausted all five
    /// attempts, because the shortage lasted longer than the retries did.
    /// </para>
    /// <para>
    /// So the number in flight is capped rather than the failures absorbed.
    /// The tests still run in parallel and the suite is no slower in practice,
    /// because the limit only binds when this many are already starting — and
    /// past that point they were not really running in parallel anyway, they
    /// were queueing inside Windows and sometimes losing.
    /// </para>
    /// </remarks>
    private static readonly SemaphoreSlim Starting =
        new(Math.Max(2, Environment.ProcessorCount / 2));

    /// <summary>Runs one command and captures everything it produced.</summary>
    public async Task<LoadoutRun> RunAsync(params string[] arguments)
    {
        for (var attempt = 0; ; attempt++)
        {
            var run = await RunOnceAsync(arguments).ConfigureAwait(false);

            var startupFailure = run.ExitCode == ProcessInitialisationFailed
                && run.StandardOutput.Length == 0
                && run.StandardError.Length == 0;

            if (!startupFailure || attempt >= 4)
            {
                return run;
            }

            // Backing off further each time. A flat wait is right for an
            // executable being scanned and wrong for a machine that is short of
            // something: five attempts a quarter of a second apart is over in
            // one second, and the shortage that failed thirty-nine tests at
            // once lasted longer than that.
            await Task.Delay(250 * (attempt + 1)).ConfigureAwait(false);
        }
    }

    private async Task<LoadoutRun> RunOnceAsync(string[] arguments)
    {
        await Starting.WaitAsync().ConfigureAwait(false);

        try
        {
            return await RunOnceCoreAsync(arguments).ConfigureAwait(false);
        }
        finally
        {
            Starting.Release();
        }
    }

    private async Task<LoadoutRun> RunOnceCoreAsync(string[] arguments)
    {
        var executable = Executable
            ?? throw new InvalidOperationException(
                "The command line has not been built. Run: dotnet build src/Loadout.Cli");

        EnsureCurrent(executable);

        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _home,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Isolate(start);

        using var process = StartWithRetry(start);

        // Read both streams before waiting. A process that fills a redirected
        // pipe blocks writing to it, and waiting first would deadlock against
        // exactly the verbose output these tests care about.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync().ConfigureAwait(false);

        return new LoadoutRun(
            process.ExitCode,
            await output.ConfigureAwait(false),
            await error.ConfigureAwait(false));
    }

    /// <summary>
    /// Starts the process, retrying briefly if the file is momentarily
    /// unavailable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An executable that has just been written can refuse to start for a
    /// moment on Windows while it is scanned. Running the whole suite straight
    /// after a build occasionally failed a scattering of these tests, and the
    /// same tests passed on the next run with nothing changed.
    /// </para>
    /// <para>
    /// Only the launch is retried, and only for the errors that mean "not
    /// available yet". Nothing about the command's behaviour is retried, so
    /// this cannot turn a real failure into a pass — a command that starts and
    /// then does the wrong thing still fails, once.
    /// </para>
    /// </remarks>
    private static Process StartWithRetry(ProcessStartInfo start)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return Process.Start(start)
                    ?? throw new InvalidOperationException(
                        "The command line could not be started.");
            }
            catch (System.ComponentModel.Win32Exception) when (attempt < 4)
            {
                Thread.Sleep(150);
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(150);
            }
        }
    }

    /// <summary>
    /// Refuses to run a command line built from different code than the tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These tests execute an artefact rather than calling into an assembly,
    /// which is the point — but it means the thing being tested and the thing
    /// just compiled can come apart. An executable left over from an earlier
    /// build passes or fails according to source nobody is looking at any more,
    /// which is worse than a failure because it looks like an answer.
    /// </para>
    /// <para>
    /// Compared by content, not by timestamp. Timestamps cannot tell "stale"
    /// apart from "correctly not rebuilt": building the test project alone
    /// gives its copy a new write time while the command line keeps its older
    /// one, and a guard comparing those declared everything stale on the first
    /// run after any build. The assembly beside the tests is copied from the
    /// command line's own build, so when the two are in step the files are
    /// byte-for-byte identical and when they are not they differ.
    /// </para>
    /// </remarks>
    private static void EnsureCurrent(string executable)
    {
        var mine = Path.Combine(AppContext.BaseDirectory, "loadout.dll");
        var theirs = Path.Combine(Path.GetDirectoryName(executable)!, "loadout.dll");

        if (!File.Exists(mine) || !File.Exists(theirs))
        {
            // A single-file publish carries no separate assembly to compare
            // against, and neither does a tree that has not been built the way
            // this expects. Say nothing rather than guess.
            return;
        }

        if (Same(mine, theirs))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The command line at {executable} was built from different code than these tests. "
            + "It would be tested against source that is no longer here. "
            + "Run: dotnet build");
    }

    private static bool Same(string first, string second) =>
        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(first))
            .SequenceEqual(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(second)));

    /// <summary>
    /// Points every storage root at the throwaway home, on whichever platform
    /// this is running.
    /// </summary>
    private void Isolate(ProcessStartInfo start)
    {
        start.Environment["USERPROFILE"] = _home;
        start.Environment["HOME"] = _home;

        // Windows keeps roaming settings and machine-local state apart, and the
        // launcher relies on that split, so both are redirected rather than one.
        start.Environment["APPDATA"] = Path.Combine(_home, "Roaming");
        start.Environment["LOCALAPPDATA"] = Path.Combine(_home, "Local");

        start.Environment["XDG_CONFIG_HOME"] = Path.Combine(_home, "config");
        start.Environment["XDG_DATA_HOME"] = Path.Combine(_home, "data");
        start.Environment["XDG_STATE_HOME"] = Path.Combine(_home, "state");
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(_home, "cache");

        // Colour would put escape sequences through the middle of the JSON on a
        // console that reports as capable.
        start.Environment["NO_COLOR"] = "1";
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_home))
            {
                Directory.Delete(_home, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing a run over.
        }
    }
}

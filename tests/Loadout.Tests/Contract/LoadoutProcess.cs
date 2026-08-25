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

                // The runtime identifier subfolder is not known here and differs
                // per machine, so it is searched for rather than assumed.
                var found = Directory
                    .EnumerateFiles(cli, name, SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    /// <summary>Runs one command and captures everything it produced.</summary>
    public async Task<LoadoutRun> RunAsync(params string[] arguments)
    {
        var executable = Executable
            ?? throw new InvalidOperationException(
                "The command line has not been built. Run: dotnet build src/Loadout.Cli");

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

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The command line could not be started.");

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

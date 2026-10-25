using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// What Studio reads, said by a running daemon that runs the command inside
/// itself, against what the same line says run as a launcher of its own.
/// </summary>
/// <remarks>
/// <para>
/// The daemon used to start a child for every read. It runs them in-process
/// now, and a page shows what the command says only if the two are the same:
/// the same document, the same exit code. Compared on a real daemon in a
/// throwaway home, because what can differ - the streams, the console, the
/// directory a command thinks it was started in - is exactly what a test
/// running the host in its own process would not see.
/// </para>
/// <para>
/// And at the end, that doing all of it started no launcher as a child.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class InProcessParityContractTests
{
    private const string Slug = "parity";

    /// <summary>The lines Studio's pages read with, each without the --json readJson adds.</summary>
    private static readonly string[][] Read =
    [
        ["project list"],
        ["project show", Slug],
        ["project worktrees", Slug],
        ["task list", "--project", Slug],
        ["task list", "--project", Slug, "--all"],
        ["instructions list", Slug],
        ["rules list", Slug],
        ["rules budget", Slug],
        ["profile list", Slug],
        ["style list", Slug],
        ["repo check", Slug],
        ["drift", Slug],
        ["docs audit", Slug],
        ["share candidates", "--project", Slug],
        ["memory find", "release", "--project", Slug],
        ["memory list", Slug],
        ["sessions", "--limit", "12"],
        ["launches", "--days", "7"],
        ["trigger journal", "--limit", "25"],
        ["trigger list"],
        ["find", "--query=release", "--limit", "5"],
        ["usage", "--days", "30"],
        ["team list"],
        ["team runs"],
        ["idea list"],
        ["config list"],
        ["events"],
        ["project show", "no-such-project"],
    ];

    [BuiltCliFact]
    public async Task Every_read_says_the_same_in_the_daemon_as_on_its_own()
    {
        using var loadout = new LoadoutProcess();

        var repository = Path.Combine(loadout.Home, Slug);

        await RepositoryAsync(repository);

        var added = await loadout.RunAsync("project", "add", repository, "--non-interactive");

        added.ExitCode.Should().Be(0, added.StandardError);

        await using var daemon = await Daemon.StartAsync(loadout);

        // A launcher of its own each time, not one handing the line to the
        // daemon being compared with.
        loadout.Environment["LOADOUT_LOCAL"] = "1";

        // Every command Studio's coverage says only reads, with nothing after
        // it, as well: most refuse for want of an argument, and the refusal
        // has to be the same document too.
        var lines = Read
            .Concat(StudioCoverage.Reach
                .Where(one => one.Value.Effect == StudioEffect.Reads)
                .Select(one => new[] { one.Key }))
            .ToList();

        var differences = new List<string>();

        foreach (var line in lines)
        {
            var path = line[0];
            List<string> words = [.. line.Skip(1), "--json"];

            var alone = await loadout.RunAsync([.. path.Split(' '), .. words, "--non-interactive"]);
            var inside = await daemon.ReadAsync(path, words);

            var expected = Timeless(Lines(alone.StandardOutput));
            var said = Timeless(inside.Output);

            if (inside.ExitCode != alone.ExitCode || said != expected)
            {
                differences.Add(
                    $"loadout {string.Join(' ', line)}: exit {alone.ExitCode} alone, {inside.ExitCode} in the daemon\n"
                    + $"--- alone\n{expected}\n--- in the daemon\n{said}\n--- daemon's error\n{inside.Error}");
            }
        }

        // And as jobs, the way Studio's buttons and the Commands page run
        // them, in the words a person reads rather than a document: tables,
        // wrapping and colour are what a console made in the daemon could get
        // wrong where a child's never did.
        foreach (var line in Jobs)
        {
            var path = line[0];
            List<string> words = [.. line.Skip(1)];

            var alone = await loadout.RunAsync([.. path.Split(' '), .. words, "--non-interactive"]);
            var inside = await daemon.JobAsync(path, words);

            var expected = Timeless(Lines(alone.StandardOutput));
            var said = Timeless(inside.Output);

            if (inside.ExitCode != alone.ExitCode || said != expected)
            {
                differences.Add(
                    $"job loadout {string.Join(' ', line)}: exit {alone.ExitCode} alone, {inside.ExitCode} in the daemon\n"
                    + $"--- alone\n{expected}\n--- in the daemon\n{said}\n--- daemon's error\n{inside.Error}");
            }
        }

        differences.Should().BeEmpty(string.Join("\n\n", differences));

        (await daemon.LauncherChildrenAsync()).Should().Be(0, "every command ran inside the daemon");
    }

    [BuiltCliFact]
    public async Task A_command_typed_while_a_daemon_runs_is_run_by_it_and_says_the_same()
    {
        using var loadout = new LoadoutProcess();

        var repository = Path.Combine(loadout.Home, Slug);

        await RepositoryAsync(repository);

        var added = await loadout.RunAsync("project", "add", repository, "--non-interactive");

        added.ExitCode.Should().Be(0, added.StandardError);

        await using var daemon = await Daemon.StartAsync(loadout);

        string[][] typed =
        [
            ["project", "list"],
            ["project", "list", "--json"],
            ["project", "show", Slug, "--json"],
            ["project", "show", "no-such-project"],
        ];

        foreach (var line in typed)
        {
            var handed = await loadout.RunAsync(line);

            loadout.Environment["LOADOUT_LOCAL"] = "1";

            var here = await loadout.RunAsync(line);

            loadout.Environment.Remove("LOADOUT_LOCAL");

            handed.ExitCode.Should().Be(here.ExitCode, string.Join(' ', line));
            Timeless(handed.StandardOutput).Should().Be(Timeless(here.StandardOutput), string.Join(' ', line));
            handed.StandardError.Trim().Should().Be(here.StandardError.Trim(), string.Join(' ', line));
        }

        (await daemon.LogAsync()).Should().Contain("from a terminal: loadout project list",
            "the daemon names each command a terminal handed it")
            .And.Contain("from a terminal: loadout project show");

        // And one that asks before it changes something stays here.
        (await daemon.LogAsync()).Should().NotContain("from a terminal: loadout project add");
    }

    /// <summary>Lines run as jobs, in their human form.</summary>
    private static readonly string[][] Jobs =
    [
        ["project list"],
        ["project show", Slug],
        ["config list"],
        ["team list"],
        ["task declare", "no-such-task", "doing", "--dry-run"],
        ["memory write", "parity-note", "--project", Slug, "--fact", "A fact.", "--dry-run"],
        ["project show", "no-such-project"],
    ];

    /// <summary>A child's output as the daemon keeps a job's: one line feed after each line.</summary>
    private static string Lines(string output)
    {
        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Concat(lines.Select(line => CommandJobs.Checked(line) + "\n"));
    }

    /// <summary>
    /// The output with every moment in it replaced, because a command that
    /// says "the last thirty days" works them out from when it ran, and two
    /// runs are never at the same instant.
    /// </summary>
    private static string Timeless(string output) =>
        System.Text.RegularExpressions.Regex.Replace(
            output, @"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?(Z|\\u002B\d\d:\d\d|[+-]\d\d:\d\d)?", "<moment>");

    private static async Task RepositoryAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(Path.Combine(directory, "README.md"), "# Parity\n");

        await GitAsync(directory, "init");
        await GitAsync(directory, "add", "-A");
        await GitAsync(directory, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-m", "first");
    }

    private static async Task GitAsync(string directory, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;

        await process.WaitForExitAsync();
    }

    /// <summary>A daemon serving the dashboard in the throwaway home, stopped when disposed.</summary>
    private sealed class Daemon : IAsyncDisposable
    {
        private readonly LoadoutProcess _loadout;
        private readonly HttpClient _client;

        private Daemon(LoadoutProcess loadout, HttpClient client)
        {
            _loadout = loadout;
            _client = client;
        }

        internal static async Task<Daemon> StartAsync(LoadoutProcess loadout)
        {
            var port = FreePort();

            // Nobody watching, so it starts in the background and returns.
            var started = await loadout.RunAsync("daemon", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));

            started.ExitCode.Should().Be(0, started.StandardOutput + started.StandardError);

            var note = Path.Combine(await StateAsync(loadout), "teams", "daemon.json");
            var token = await TokenAsync(note);

            var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            client.DefaultRequestHeaders.Add("X-Loadout-Token", token);

            return new Daemon(loadout, client);
        }

        internal async Task<(int? ExitCode, string Output, string Error)> ReadAsync(string path, IReadOnlyList<string> words)
        {
            using var response = await _client.PostAsJsonAsync(CommandsApi.ReadRoot, new { path, words });

            var body = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.OK, $"'{path}' should be read: {body}");

            var shown = JsonDocument.Parse(body).RootElement;

            return (
                shown.GetProperty("exitCode").ValueKind == JsonValueKind.Number ? shown.GetProperty("exitCode").GetInt32() : null,
                shown.GetProperty("output").GetString() ?? string.Empty,
                shown.GetProperty("error").GetString() ?? string.Empty);
        }

        internal async Task<(int? ExitCode, string Output, string Error)> JobAsync(string path, IReadOnlyList<string> words)
        {
            using var response = await _client.PostAsJsonAsync(CommandsApi.JobsRoot, new { path, words });

            var body = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.Accepted, $"'{path}' should start: {body}");

            var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString();
            var until = DateTime.UtcNow.AddMinutes(2);

            while (DateTime.UtcNow < until)
            {
                var shown = JsonDocument.Parse(await _client.GetStringAsync($"{CommandsApi.JobsRoot}/{id}")).RootElement;

                if (shown.GetProperty("state").GetString() != "running")
                {
                    return (
                        shown.GetProperty("exitCode").ValueKind == JsonValueKind.Number ? shown.GetProperty("exitCode").GetInt32() : null,
                        shown.GetProperty("output").GetString() ?? string.Empty,
                        shown.GetProperty("error").GetString() ?? string.Empty);
                }

                await Task.Delay(50);
            }

            throw new TimeoutException($"'{path}' was still running after two minutes.");
        }

        /// <summary>What the daemon has written to its log so far.</summary>
        internal async Task<string> LogAsync()
        {
            var log = Path.Combine(await StateAsync(_loadout), "teams", "daemon.log");

            // Shared, because the daemon has it open.
            await using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync();
        }

        internal async Task<long> LauncherChildrenAsync()
        {
            var about = JsonDocument.Parse(await _client.GetStringAsync("/api/v1/about")).RootElement;

            return about.GetProperty("launcherChildren").GetInt64();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();

            var stopped = await _loadout.RunAsync("daemon", "stop", "--now");

            stopped.ExitCode.Should().Be(0, stopped.StandardOutput + stopped.StandardError);
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        /// <summary>The token from the address the daemon wrote, once it has written one.</summary>
        private static async Task<string> TokenAsync(string note)
        {
            var until = DateTime.UtcNow.AddSeconds(30);

            while (DateTime.UtcNow < until)
            {
                if (File.Exists(note))
                {
                    try
                    {
                        var address = JsonDocument.Parse(await File.ReadAllTextAsync(note))
                            .RootElement.GetProperty("Address").GetString();

                        if (address is { Length: > 0 } && new Uri(address).Query is { Length: > 0 } query
                            && System.Web.HttpUtility.ParseQueryString(query)["token"] is { Length: > 0 } token)
                        {
                            return token;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException)
                    {
                        // Still being written.
                    }
                }

                await Task.Delay(100);
            }

            throw new TimeoutException($"The daemon wrote no address to {note} within thirty seconds.");
        }

        private static async Task<string> StateAsync(LoadoutProcess loadout)
        {
            var doctor = await loadout.RunAsync("doctor", "--json");

            return doctor.Json()
                .GetProperty("checks")
                .EnumerateArray()
                .First(check => check.GetProperty("name").GetString() == "State")
                .GetProperty("detail")
                .GetString()!;
        }
    }
}

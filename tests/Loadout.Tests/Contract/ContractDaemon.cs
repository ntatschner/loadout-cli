using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams.Daemon;

namespace Loadout.Tests.Contract;

/// <summary>A daemon serving the dashboard in the throwaway home, stopped when disposed.</summary>
internal sealed class ContractDaemon : IAsyncDisposable
{
    private readonly LoadoutProcess _loadout;
    private readonly HttpClient _client;

    private ContractDaemon(LoadoutProcess loadout, HttpClient client)
    {
        _loadout = loadout;
        _client = client;
    }

    internal static async Task<ContractDaemon> StartAsync(LoadoutProcess loadout)
    {
        var port = FreePort();

        // Nobody watching, so it starts in the background and returns.
        var started = await loadout.RunAsync("daemon", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        started.ExitCode.Should().Be(0, started.StandardOutput + started.StandardError);

        var note = Path.Combine(await StateAsync(loadout), "teams", "daemon.json");
        var token = await TokenAsync(note);

        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        client.DefaultRequestHeaders.Add("X-Loadout-Token", token);

        return new ContractDaemon(loadout, client);
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

    /// <summary>
    /// Runs a line as the command line hands one over when it is typed in a
    /// terminal while the daemon runs: the words, and the directory they were
    /// typed in, which need not be the daemon's.
    /// </summary>
    internal async Task<(int? ExitCode, string Output, string Error)> TypedAsync(IReadOnlyList<string> words, string directory)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RunApi.Root)
        {
            Content = JsonContent.Create(new
            {
                version = typeof(RunApi).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                words,
                directory,
                width = 120,
                ansi = false,
                colours = 0,
            }),
        };

        using var response = await _client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"'{string.Join(' ', words)}' should be run: {body}");

        var frames = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).ToList();

        string Joined(string stream) =>
            string.Concat(frames.Where(frame => frame.TryGetProperty(stream, out _)).Select(frame => frame.GetProperty(stream).GetString()));

        var exit = frames.LastOrDefault(frame => frame.TryGetProperty("exit", out _));

        return (exit.ValueKind == JsonValueKind.Object ? exit.GetProperty("exit").GetInt32() : null, Joined("out"), Joined("err"));
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

    /// <summary>A client that reaches the daemon with its token, for asking it anything a page would.</summary>
    internal HttpClient Client => _client;

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

    /// <summary>A Git repository with one commit, for a project to be registered at.</summary>
    internal static async Task RepositoryAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(Path.Combine(directory, "README.md"), "# Parity\n");

        await GitAsync(directory, "init");
        await GitAsync(directory, "add", "-A");
        await GitAsync(directory, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-m", "first");
    }

    internal static async Task GitAsync(string directory, params string[] arguments)
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
}

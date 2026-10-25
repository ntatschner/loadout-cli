using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams.Daemon;
using Xunit;
using Xunit.Abstractions;

namespace Loadout.Tests.Contract;

/// <summary>
/// Every Studio page, as the requests it makes when it opens, against a warm
/// daemon: each page has to have its answers within half a second.
/// </summary>
/// <remarks>
/// <para>
/// Run only when asked, with <c>LOADOUT_TIMED_PAGES=1</c>, and meant for a
/// Release build: a debug build and a busy machine measure something else.
/// It is a check on the daemon, not on the browser - each page's requests are
/// sent together, as the page sends them, and the page counts as drawn when
/// the last has answered.
/// </para>
/// <para>
/// The requests were taken from Studio itself, opening each page in a browser
/// against a daemon and recording what it asked for, so they are what the page
/// does rather than what its source suggests. A page that starts asking for
/// something new is not caught here until it is added; the test below this
/// one catches the other direction, a request no page makes any more.
/// </para>
/// <para>
/// The home directory is given a month of invented transcripts, about as many
/// as a busy machine has, so 'usage', the session list and search have real
/// work to do; a fresh home would answer them instantly and prove nothing.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class TimedPagesContractTests(ITestOutputHelper output)
{
    /// <summary>The variable that asks for this check.</summary>
    internal const string Variable = "LOADOUT_TIMED_PAGES";

    /// <summary>How long a page may take to have every answer it asked for.</summary>
    internal static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(500);

    private const string Slug = "starstats";

    /// <summary>Studio opening: what Home and Commands draw from, asked once as the app starts.</summary>
    private static readonly string[] Opening =
    [
        "GET /api/v1/signed-in", "GET /api/v1/about", "GET /api/v1/preferences", "GET /api/runs", "GET /api/v1/sessions",
        "GET /api/ideas", "GET /api/tasks", "GET /api/v1/jobs", "GET /api/v1/history?limit=100", "GET /api/v1/history/lost",
        "GET /api/v1/eventlog/unread", "GET /api/v1/update",
    ];

    /// <summary>Each page and what it asks for when it opens; a read is its command and words.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> Pages = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["home"] = Opening,
        ["commands"] = Opening,
        ["sessions"] =
        [
            "GET /api/v1/history?limit=100", "GET /api/v1/history/lost", "GET /api/v1/history/query?q=&limit=100",
            "GET /api/v1/history/query?q=&limit=100",
        ],
        ["ideas"] = ["READ idea list --limit 200", "GET /api/v1/projects"],
        ["teams"] =
        [
            "READ trigger approvals", "READ trigger list", "READ trigger stale", "READ trigger sources", "READ team list",
            "GET /api/choices", "READ team schedule list", "GET /api/daemon", "READ team runs --limit 40",
            "READ team queue list bug-hunt", "READ team queue list dependency-sweep", "READ team queue list docs-crew",
            "READ team queue list iterating-project", "READ team queue list marketing-studio",
            "READ team queue list release-crew", "READ team queue list system-watch", "READ team queue list tool-works",
        ],
        ["events"] = ["GET /api/v1/eventlog?limit=200"],
        ["memory"] =
        [
            "GET /api/v1/projects", "GET /api/v1/commands", $"GET /api/v1/projects/{Slug}/memory", $"READ memory audit {Slug}",
        ],
        ["context"] =
        [
            "READ project list", $"READ instructions list {Slug}", $"READ instructions explain --project {Slug} --mode implement",
        ],
        ["spend"] =
        [
            "READ usage --by project --days 30", "READ launches --days 7", "READ running", "READ sessions --limit 12",
            "READ telemetry status", "READ status",
        ],
        ["projects"] =
        [
            "GET /api/v1/history?limit=100", "GET /api/v1/history/lost", "READ project list", $"READ task list --project {Slug}",
            "READ drift", "READ idea list",
        ],
        ["settings"] = ["READ daemon autostart show"],
    };

    [TimedPagesFact]
    public async Task Every_page_has_its_answers_within_half_a_second()
    {
        using var loadout = new LoadoutProcess();

        var repository = Path.Combine(loadout.Home, Slug);

        await ContractDaemon.RepositoryAsync(repository);

        var added = await loadout.RunAsync("project", "add", repository, "--non-interactive");

        added.ExitCode.Should().Be(0, added.StandardError);

        await TranscriptsAsync(loadout.Home);

        await using var daemon = await ContractDaemon.StartAsync(loadout);

        // Warm, as a daemon is by the time anybody opens a second page: every
        // page once, and nothing measured.
        foreach (var requests in Pages.Values)
        {
            await PageAsync(daemon.Client, requests);
        }

        var report = new StringBuilder();
        var slow = new List<string>();

        foreach (var (page, requests) in Pages)
        {
            // The best of three, because one slow answer on a shared machine
            // says more about the machine than the daemon.
            var times = new List<TimeSpan>();

            for (var i = 0; i < 3; i++)
            {
                times.Add(await PageAsync(daemon.Client, requests));
            }

            var best = times.Min();

            report.AppendLine($"{page,-10} {best.TotalMilliseconds,6:F0} ms  (of {string.Join(", ", times.Select(time => time.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)))})");

            if (best > Budget)
            {
                slow.Add($"{page}: {best.TotalMilliseconds:F0} ms");
            }
        }

        output.WriteLine(report.ToString());

        slow.Should().BeEmpty($"every page should have its answers within {Budget.TotalMilliseconds} ms:\n{report}");

        (await daemon.LauncherChildrenAsync()).Should().Be(0, "opening every page started no launcher");
    }

    [Fact]
    public void Every_request_timed_is_one_Studio_still_makes()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Path.Exists(Path.Combine(root.FullName, ".git")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository holds Studio's source");

        var source = string.Join('\n', Directory
            .GetFiles(Path.Combine(root!.FullName, "studio", "src"), "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".svelte", StringComparison.Ordinal)
                || (file.EndsWith(".ts", StringComparison.Ordinal) && !file.EndsWith(".test.ts", StringComparison.Ordinal)))
            .Select(File.ReadAllText));

        var gone = Pages.Values.SelectMany(requests => requests).Distinct()
            .Where(request => request.StartsWith("READ ", StringComparison.Ordinal)
                ? !source.Contains($"'{Read(request).Path}'", StringComparison.Ordinal)
                : !source.Contains(Endpoint(request[4..]), StringComparison.Ordinal))
            .ToList();

        gone.Should().BeEmpty("a request no page makes any more times nothing a person waits for; take it out of Pages");
    }

    /// <summary>A request's path up to any query, and with a project's name in it written as Studio builds it.</summary>
    private static string Endpoint(string path)
    {
        var bare = path.Split('?')[0];

        return bare.Contains(Slug, StringComparison.Ordinal) ? bare[..bare.IndexOf(Slug, StringComparison.Ordinal)] : bare;
    }

    /// <summary>Sends a page's requests together and waits for the last answer.</summary>
    private static async Task<TimeSpan> PageAsync(HttpClient client, IReadOnlyList<string> requests)
    {
        var clock = Stopwatch.StartNew();

        await Task.WhenAll(requests.Select(async request =>
        {
            using var response = await client.SendAsync(Request(request));
            var body = await response.Content.ReadAsStringAsync();

            response.IsSuccessStatusCode.Should().BeTrue($"'{request}' should be answered: {body}");

            if (request.StartsWith("READ ", StringComparison.Ordinal))
            {
                JsonDocument.Parse(body).RootElement.GetProperty("state").GetString()
                    .Should().NotBe("running", $"'{request}' should have finished, not been cut off");
            }
        }));

        return clock.Elapsed;
    }

    private static HttpRequestMessage Request(string request)
    {
        if (request.StartsWith("GET ", StringComparison.Ordinal))
        {
            return new HttpRequestMessage(HttpMethod.Get, request[4..]);
        }

        var (path, words) = Read(request);

        return new HttpRequestMessage(HttpMethod.Post, CommandsApi.ReadRoot)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { path, words = words.Append("--json") }), Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>A read's command and the words after it: the command is every word before the first that is not part of one.</summary>
    internal static (string Path, string[] Words) Read(string request)
    {
        var words = request["READ ".Length..].Split(' ');

        for (var take = Math.Min(3, words.Length); take > 0; take--)
        {
            var path = string.Join(' ', words.Take(take));

            if (StudioCoverage.Reach.ContainsKey(path))
            {
                return (path, words[take..]);
            }
        }

        throw new InvalidOperationException($"'{request}' does not start with a command Studio knows.");
    }

    /// <summary>
    /// A month of invented transcripts: four hundred conversations of a couple
    /// of hundred messages, each with what a tool said, in a hundred working
    /// directories, twenty of them repositories that exist.
    /// </summary>
    private static async Task TranscriptsAsync(string home)
    {
        var directories = new List<string>();

        for (var i = 0; i < 20; i++)
        {
            var directory = Path.Combine(home, "checkouts", $"storefront-{i}");

            Directory.CreateDirectory(directory);
            await ContractDaemon.GitAsync(directory, "init");
            directories.Add(directory);
        }

        for (var i = 0; i < 80; i++)
        {
            directories.Add(Path.Combine(home, "gone", $"scratch-{i}"));
        }

        var today = DateTime.UtcNow.Date;

        // What makes a real transcript heavy is what tools said, not the
        // accounting: a month of them weighs well over a gigabyte. A fixture
        // without it reads so fast that nothing kept or not kept would show.
        var said = JsonSerializer.Serialize(new string('x', 4096));

        for (var conversation = 0; conversation < 400; conversation++)
        {
            var directory = directories[conversation % directories.Count];
            var folder = Path.Combine(home, ".claude", "projects", "-" + Path.GetFileName(directory));

            Directory.CreateDirectory(folder);

            var lines = new StringBuilder();
            var day = today.AddDays(-(conversation % 30));

            for (var message = 0; message < 230; message++)
            {
                var at = day.AddMinutes(message).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
                var cwd = JsonSerializer.Serialize(directory);

                lines.Append($"{{\"type\":\"user\",\"timestamp\":\"{at}\",\"cwd\":{cwd},\"message\":{{\"content\":\"step {message}\"}}}}\n");
                lines.Append($"{{\"type\":\"user\",\"timestamp\":\"{at}\",\"cwd\":{cwd},\"toolUseResult\":{said}}}\n");
                lines.Append($"{{\"type\":\"assistant\",\"timestamp\":\"{at}\",\"cwd\":{cwd},\"message\":{{\"id\":\"msg_{conversation}_{message}\",\"model\":\"claude-opus-5\","
                    + "\"usage\":{\"input_tokens\":120,\"output_tokens\":40,\"cache_read_input_tokens\":9000,"
                    + "\"cache_creation\":{\"ephemeral_5m_input_tokens\":300,\"ephemeral_1h_input_tokens\":0}}}}\n");
            }

            var file = Path.Combine(folder, Guid.NewGuid().ToString() + ".jsonl");

            await File.WriteAllTextAsync(file, lines.ToString());
            File.SetLastWriteTimeUtc(file, day.AddHours(12));
        }
    }
}

/// <summary>A fact run only when the timed check is asked for, against a built command line.</summary>
public sealed class TimedPagesFactAttribute : FactAttribute
{
    public TimedPagesFactAttribute()
    {
        if (LoadoutProcess.Executable is null)
        {
            Skip = "The command line has not been built. Run: dotnet build src/Loadout.Cli -c Release";
        }
        else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(TimedPagesContractTests.Variable)))
        {
            Skip = $"Timed only when asked: set {TimedPagesContractTests.Variable}=1, on a Release build.";
        }
    }
}

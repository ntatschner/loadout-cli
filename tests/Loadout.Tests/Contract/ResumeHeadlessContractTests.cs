using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// A team node's conversation, hidden from every resume list, can still be
/// taken over by naming it.
/// </summary>
/// <remarks>
/// <para>
/// Planted rather than made: a session line in the ledger and a transcript in
/// the store a finished node is moved to, exactly as the launcher leaves them.
/// Claude's folder is the throwaway home's, through <c>CLAUDE_CONFIG_DIR</c>.
/// </para>
/// <para>
/// The resume that is not a dry run is given a <c>PATH</c> holding git's own
/// directory and nothing else, so no agent can be found and nothing is
/// started. The transcript is put back
/// before the agent is looked for, which is what lets this assert it without
/// running Claude on the machine running the suite.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class ResumeHeadlessContractTests
{
    private const string Node = "5d1c9a20-node";

    [BuiltCliFact]
    public async Task A_node_named_outright_is_put_back_and_found()
    {
        using var loadout = new LoadoutProcess();

        var (project, kept, home) = await PlantAsync(loadout);

        // Git's own directory and nothing else on the path: git is what
        // tells which project a session belongs to, and no agent is there,
        // so the launch stops at finding one.
        loadout.Environment["PATH"] = GitDirectory();

        var run = await loadout.RunAsync("resume", Node, "--project", project);

        var said = run.StandardOutput + run.StandardError;

        run.ExitCode.Should().NotBe(0, "no agent was on the path, so nothing was started: " + said);

        said.Should().NotContain("No recorded agent sessions", "the node was named, so it is not hidden");
        said.Should().NotContain(Node + "' matches", "the node was named, so it is not hidden");

        File.Exists(Path.Combine(home, Node + ".jsonl")).Should().BeTrue(
            "the agent resumes from its own folder, so the transcript has to be back there");
        File.Exists(kept).Should().BeFalse();
    }

    [BuiltCliFact]
    public async Task A_dry_run_moves_nothing()
    {
        using var loadout = new LoadoutProcess();

        var (project, kept, home) = await PlantAsync(loadout);

        await loadout.RunAsync("resume", Node, "--project", project, "--dry-run");

        File.Exists(kept).Should().BeTrue("--dry-run means change nothing");
        File.Exists(Path.Combine(home, Node + ".jsonl")).Should().BeFalse();
    }

    [BuiltCliFact]
    public async Task A_node_is_left_out_of_the_session_list()
    {
        using var loadout = new LoadoutProcess();

        var (project, kept, home) = await PlantAsync(loadout);

        // Where the node's transcript would be if it had never been moved:
        // in Claude's folder, which the list reads.
        Directory.CreateDirectory(home);
        File.Move(kept, Path.Combine(home, Node + ".jsonl"));

        var run = await loadout.RunAsync("sessions", "--project", project, "--json");

        run.StandardOutput.Should().NotContain(Node, "nobody started it, and the run is where it is found");
    }

    /// <summary>
    /// A registered project, a session line naming the node, and the node's
    /// transcript where a finished node's is kept.
    /// </summary>
    private static async Task<(string Project, string Kept, string Home)> PlantAsync(LoadoutProcess loadout)
    {
        loadout.Environment["CLAUDE_CONFIG_DIR"] = Path.Combine(loadout.Home, "claude");

        var repository = Path.Combine(loadout.Home, "a-repository");
        Directory.CreateDirectory(repository);

        await Git(repository, "init");
        await File.WriteAllTextAsync(Path.Combine(repository, "readme.md"), "x");
        await Git(repository, "add", "-A");
        await Git(repository, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-m", "first");

        var added = await loadout.RunAsync("project", "add", repository, "--json");
        added.ExitCode.Should().Be(0, added.StandardError);

        var state = State(loadout.Home);

        Directory.CreateDirectory(Path.Combine(state, "launches"));
        await File.WriteAllTextAsync(
            Path.Combine(state, "launches", "ledger.jsonl"),
            $$"""{"Kind":"session","Id":"{{Node}}","When":"2026-10-07T09:00:00.0000000+00:00","Agent":"claude","Purpose":"headless launch"}""" + "\n");

        var folder = "a-repository-folder";
        var kept = Path.Combine(state, "headless", "transcripts", folder, Node + ".jsonl");

        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);

        var line = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "user",
            sessionId = Node,
            cwd = repository,
            timestamp = "2026-10-07T09:00:00.000Z",
            message = new { role = "user", content = "the lead's brief" },
        });

        await File.WriteAllTextAsync(kept, line + "\n");

        return (added.Json().GetProperty("id").GetString()!, kept, Path.Combine(loadout.Home, "claude", "projects", folder));
    }

    /// <summary>The launcher's state directory under a throwaway home, as each platform places it.</summary>
    [BuiltCliTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Team_status_offers_to_take_a_node_over_only_once_its_run_has_ended(bool ended)
    {
        using var loadout = new LoadoutProcess();

        const string Run = "20261007-0900-ab12";

        var directory = Path.Combine(State(loadout.Home), "teams", "runs", Run);
        Directory.CreateDirectory(directory);

        string[] lines =
        [
            """{"at":"2026-10-07T09:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"iterating-project","goal":"Add --since","autonomy":"supervised","rounds":3,"project":"storefront"}}""",
            """{"at":"2026-10-07T09:00:02+00:00","run":"r","node":"lead","kind":"node.launched","data":{"role":"role.project-lead"}}""",
            $$$"""{"at":"2026-10-07T09:00:40+00:00","run":"r","node":"lead","kind":"node.turn","data":{"round":1,"turns":4,"cost":0.42,"session":"{{{Node}}}"}}""",
            """{"at":"2026-10-07T09:00:41+00:00","run":"r","node":"lead","kind":"node.ended","data":{"exit":0}}""",
            .. ended
                ? new[] { """{"at":"2026-10-07T09:05:00+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"done","outcome":"done","cost":0.42,"rounds":1}}""" }
                : [],
        ];

        await File.WriteAllLinesAsync(Path.Combine(directory, "journal.jsonl"), lines);

        var shown = await loadout.RunAsync("team", "status", Run);
        var json = (await loadout.RunAsync("team", "status", Run, "--json")).Json();

        var command = $"loadout resume {Node} --project storefront";
        var node = json.GetProperty("nodes")[0];

        node.GetProperty("session").GetString().Should().Be(Node);

        if (ended)
        {
            shown.StandardOutput.Should().Contain("take over: " + command);
            node.GetProperty("takeOver").GetString().Should().Be(command);
        }
        else
        {
            shown.StandardOutput.Should().NotContain("take over", "a run still going resumes its own nodes");
            // Left out, the way --json leaves out every value it has not got.
            (node.TryGetProperty("takeOver", out var offered)
                && offered.ValueKind != System.Text.Json.JsonValueKind.Null).Should().BeFalse();
        }
    }

    [BuiltCliFact]
    public async Task A_node_from_a_run_before_the_ledger_listened_is_caught_up_on()
    {
        using var loadout = new LoadoutProcess();

        var (project, kept, home) = await PlantAsync(loadout);

        // No ledger line this time, and the transcript still in Claude's
        // folder: what a run from before this change left behind. Only its
        // journal says whose the conversation was.
        var state = State(loadout.Home);
        File.Delete(Path.Combine(state, "launches", "ledger.jsonl"));
        Directory.CreateDirectory(home);
        File.Move(kept, Path.Combine(home, Node + ".jsonl"));

        var run = Path.Combine(state, "teams", "runs", "20260901-0900-old1");
        Directory.CreateDirectory(run);
        await File.WriteAllLinesAsync(Path.Combine(run, "journal.jsonl"),
        [
            """{"at":"2026-09-01T09:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"iterating-project","goal":"x","autonomy":"autonomous","rounds":1}}""",
            $$$"""{"at":"2026-09-01T09:00:40+00:00","run":"r","node":"lead","kind":"node.turn","data":{"round":1,"turns":1,"cost":0.1,"session":"{{{Node}}}"}}""",
            """{"at":"2026-09-01T09:05:00+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"done","outcome":"done","cost":0.1,"rounds":1}}""",
        ]);

        var listed = await loadout.RunAsync("sessions", "--project", project, "--json");

        listed.StandardOutput.Should().NotContain(Node, "its run's journal names it as a node's");
        File.Exists(Path.Combine(home, Node + ".jsonl")).Should().BeFalse(
            "a node of a run that has ended leaves Claude's folder too");
        File.Exists(kept).Should().BeTrue();
    }

    private static string State(string home)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(home, "Local", "Loadout");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", "Loadout", "state");
        }

        return Path.Combine(home, "data", "loadout");
    }

    private static string GitDirectory()
    {
        var name = OperatingSystem.IsWindows() ? "git.exe" : "git";

        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .First(directory => File.Exists(Path.Combine(directory, name)));
    }

    private static async Task Git(string directory, params string[] arguments)
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

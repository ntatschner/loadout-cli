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

using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Adding to an idea that already has a plan, from outside the process: kept
/// verbatim with who and when, shown by idea show, refused once accepted, and
/// nothing written on a dry run.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class IdeaAddToContractTests
{
    private const string Note = "- also on mobile";

    [BuiltCliFact]
    public async Task A_dry_run_names_the_note_and_writes_nothing()
    {
        using var loadout = new LoadoutProcess();
        var (project, record) = await Proposed(loadout);
        var before = await File.ReadAllBytesAsync(record);

        var tried = await loadout.RunAsync("idea", "add", "--to", "sync", $"--text={Note}", "--project", project, "--dry-run");

        tried.ExitCode.Should().Be(0, tried.StandardError);
        tried.StandardOutput.Should().Contain("Would add N1 to sync. Nothing was written.");
        (await File.ReadAllBytesAsync(record)).Should().Equal(before);
    }

    [BuiltCliFact]
    public async Task An_addition_is_kept_verbatim_and_shown()
    {
        using var loadout = new LoadoutProcess();
        var (project, _) = await Proposed(loadout);

        var added = await loadout.RunAsync("idea", "add", "--to", "sync", $"--text={Note}", "--project", project, "--by", "nigel");

        added.ExitCode.Should().Be(0, added.StandardError);
        added.StandardOutput.Should().Contain("Added N1 to sync.").And.Contain("loadout idea refine sync");

        var shown = await loadout.RunAsync("idea", "show", "sync", "--project", project, "--json");

        shown.ExitCode.Should().Be(0, shown.StandardError);
        var read = shown.Json();
        read.GetProperty("stage").GetString().Should().Be("ready", "a note no round has read is something the plan has not taken in");
        var note = read.GetProperty("notes")[0];
        note.GetProperty("text").GetString().Should().Be(Note);
        note.GetProperty("by").GetString().Should().Be("nigel");
        note.GetProperty("added").GetDateTimeOffset().Should().BeAfter(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var said = await loadout.RunAsync("idea", "show", "sync", "--project", project);

        // Spectre wraps redirected output at 80 columns, so the words are
        // compared with the wrapping taken out.
        string.Join(' ', said.StandardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("N1 by nigel").And.Contain(Note).And.Contain("not read by a round yet");
    }

    [BuiltCliTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_accepted_idea_is_refused_and_left_as_it_was(bool dryRun)
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");
        var record = await Write(loadout, project, "done-idea", $"""
            id: done-idea
            ask: A status page for the home lab.
            accepted:
              accepted_utc: 2026-10-19T10:00:00.0000000+00:00
              accepted_by: nigel
              project: {project}
              plan_path: projects/{project}/ideas/done-idea.md
            """);
        var before = await File.ReadAllBytesAsync(record);

        string[] arguments = ["idea", "add", "--to", "done-idea", "--text=more", "--project", project];
        var tried = await loadout.RunAsync(dryRun ? [.. arguments, "--dry-run"] : arguments);

        tried.ExitCode.Should().NotBe(0);
        (tried.StandardOutput + tried.StandardError).Should().Contain("has been accepted");
        (await File.ReadAllBytesAsync(record)).Should().Equal(before);
    }

    [BuiltCliFact]
    public async Task To_with_id_is_refused()
    {
        using var loadout = new LoadoutProcess();
        var (project, record) = await Proposed(loadout);
        var before = await File.ReadAllBytesAsync(record);

        var tried = await loadout.RunAsync("idea", "add", "--to", "sync", "--id", "other", "--text=more", "--project", project);

        tried.ExitCode.Should().NotBe(0);
        (tried.StandardOutput + tried.StandardError).Should().Contain("--id names a new idea");
        (await File.ReadAllBytesAsync(record)).Should().Equal(before);
    }

    [BuiltCliFact]
    public async Task An_unknown_idea_is_refused_and_the_way_to_name_a_list_is_said()
    {
        using var loadout = new LoadoutProcess();
        var (project, _) = await Proposed(loadout);

        var tried = await loadout.RunAsync("idea", "add", "--to", "nothing-here", "--text=more", "--project", project);

        tried.ExitCode.Should().NotBe(0);
        string.Join(' ', (tried.StandardOutput + tried.StandardError).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("nothing-here").And.Contain("--project");
    }

    /// <summary>
    /// A registered project with an idea on it that has a plan and nothing new
    /// since, written by hand because a real plan needs an agent's round.
    /// </summary>
    private static async Task<(string Project, string Record)> Proposed(LoadoutProcess loadout)
    {
        var project = await TaskTraceContractTests.Registered(loadout, "repository");
        var record = await Write(loadout, project, "sync", """
            id: sync
            ask: Sync my settings between machines.
            captured_utc: 2026-10-19T09:00:00.0000000+00:00
            plan:
              revision: 1
              proposed_utc: 2026-10-19T10:00:00.0000000+00:00
              title: Settings sync
              understanding: Keep settings the same everywhere.
              layers:
              - id: L1
                name: Storage
                options:
                - id: L1a
                  title: A file in the workspace
                  recommended: true
                chosen: L1a
            """);

        return (project, record);
    }

    private static async Task<string> Write(LoadoutProcess loadout, string project, string id, string yaml)
    {
        var list = Directory.EnumerateFiles(loadout.Home, "tasks.yaml", SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(Path.GetDirectoryName(path)) == project);
        var ideas = Path.Combine(Path.GetDirectoryName(list)!, "ideas");
        Directory.CreateDirectory(ideas);
        var record = Path.Combine(ideas, $"{id}.yaml");
        await File.WriteAllTextAsync(record, yaml);

        return record;
    }
}

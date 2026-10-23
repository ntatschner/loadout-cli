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
    public async Task Without_refine_and_nobody_at_the_keyboard_no_round_starts_and_the_next_step_is_said()
    {
        using var loadout = new LoadoutProcess();
        var (project, record) = await Proposed(loadout);

        // Were a round started by mistake, it would fail here rather than pay an agent.
        loadout.Environment["PATH"] = GitDirectory();

        var added = await loadout.RunAsync(
            "idea", "add", "--to", "sync", $"--text={Note}", "--project", project, "--non-interactive", "--agent", "no-such-agent", "--json");

        added.ExitCode.Should().Be(0, added.StandardError);
        var json = added.Json();
        json.GetProperty("added").GetString().Should().Be("N1");
        json.GetProperty("refine").GetProperty("requested").GetBoolean().Should().BeFalse();
        json.GetProperty("refine").GetProperty("next").GetString().Should().Be("loadout idea refine sync");
        json.GetProperty("unanswered").GetInt32().Should().Be(0);
        json.GetProperty("stage").GetString().Should().Be("ready", "the note waits for a round nobody started");
        (await File.ReadAllTextAsync(record)).Should().Contain(Note);

        // --json leaves nulls out, so a round that was never asked for leaves
        // neither a failure nor a last round to read.
        (json.TryGetProperty("failure", out var failure) && failure.ValueKind != System.Text.Json.JsonValueKind.Null)
            .Should().BeFalse("no round was asked for");
        (json.TryGetProperty("lastRound", out var last) && last.ValueKind != System.Text.Json.JsonValueKind.Null)
            .Should().BeFalse("no round was asked for");
    }

    [BuiltCliFact]
    public async Task With_refine_a_round_is_asked_for_after_the_note_is_saved_and_its_failure_is_not_the_adds()
    {
        using var loadout = new LoadoutProcess();
        var (project, record) = await Proposed(loadout);

        // An agent that does not exist, and no agent on the path either, so the
        // round is asked for and fails without anything being started or paid for.
        loadout.Environment["PATH"] = GitDirectory();

        var added = await loadout.RunAsync(
            "idea", "add", "--to", "sync", $"--text={Note}", "--project", project, "--refine", "--agent", "no-such-agent");

        added.ExitCode.Should().NotBe(0, "the round failed");
        added.StandardOutput.Should().Contain("Added N1 to sync.");
        string.Join(' ', added.StandardError.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("N1 was saved on sync, but the refine failed").And.Contain("loadout idea refine sync");
        (await File.ReadAllTextAsync(record)).Should().Contain(Note, "the note is kept whatever the round did");

        var json = await loadout.RunAsync(
            "idea", "add", "--to", "sync", "--text=and dark mode", "--project", project, "--refine", "--agent", "no-such-agent", "--json");

        json.ExitCode.Should().NotBe(0);
        var refine = json.Json().GetProperty("refine");
        refine.GetProperty("requested").GetBoolean().Should().BeTrue();
        refine.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        json.Json().GetProperty("added").GetString().Should().Be("N2");
    }

    [BuiltCliFact]
    public async Task Refine_and_no_refine_together_are_refused_and_nothing_is_added()
    {
        using var loadout = new LoadoutProcess();
        var (project, record) = await Proposed(loadout);
        var before = await File.ReadAllBytesAsync(record);
        loadout.Environment["PATH"] = GitDirectory();

        var tried = await loadout.RunAsync(
            "idea", "add", "--to", "sync", "--text=more", "--project", project, "--refine", "--no-refine", "--agent", "no-such-agent");

        tried.ExitCode.Should().NotBe(0);
        tried.StandardError.Should().Contain("--refine and --no-refine");
        (await File.ReadAllBytesAsync(record)).Should().Equal(before);
    }

    [BuiltCliFact]
    public async Task Open_questions_are_counted_in_a_reminder_and_the_note_is_still_added()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");
        await Write(loadout, project, "asking", """
            id: asking
            ask: A status page for the home lab.
            rounds:
            - revision: 1
              questions:
              - id: Q1
                question: Which machines?
              - id: Q2
                question: How often?
            """);

        var added = await loadout.RunAsync("idea", "add", "--to", "asking", "--text=more", "--project", project, "--non-interactive");

        added.ExitCode.Should().Be(0, added.StandardError);
        string.Join(' ', added.StandardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("Added N1 to asking.").And.Contain("2 questions still unanswered");
    }

    [BuiltCliFact]
    public async Task Refine_with_open_questions_saves_the_note_starts_no_round_and_succeeds()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");
        var record = await Write(loadout, project, "asking", """
            id: asking
            ask: A status page for the home lab.
            rounds:
            - revision: 1
              questions:
              - id: Q1
                question: Which machines?
              - id: Q2
                question: How often?
            """);

        // An agent that does not exist, and none on the path, so a round asked
        // for would fail and the exit code would say so.
        loadout.Environment["PATH"] = GitDirectory();

        var added = await loadout.RunAsync(
            "idea", "add", "--to", "asking", "--text=more", "--project", project,
            "--refine", "--non-interactive", "--agent", "no-such-agent");

        added.ExitCode.Should().Be(0, added.StandardError);
        string.Join(' ', added.StandardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("Added N1 to asking.")
            .And.Contain("2 questions still unanswered")
            .And.Contain("waits until they are answered")
            .And.Contain("loadout idea answer asking");
        (await File.ReadAllTextAsync(record)).Should().Contain("more");

        var json = await loadout.RunAsync(
            "idea", "add", "--to", "asking", "--text=and more", "--project", project,
            "--refine", "--non-interactive", "--agent", "no-such-agent", "--json");

        json.ExitCode.Should().Be(0, json.StandardError);
        var refine = json.Json().GetProperty("refine");
        refine.GetProperty("requested").GetBoolean().Should().BeTrue();
        refine.GetProperty("waiting").GetString().Should().Be("questions");
        refine.TryGetProperty("error", out _).Should().BeFalse();
        json.Json().GetProperty("added").GetString().Should().Be("N2");
    }

    [BuiltCliFact]
    public async Task Refine_on_an_accepted_idea_is_refused_as_an_add_and_no_round_is_asked_for()
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

        var tried = await loadout.RunAsync("idea", "add", "--to", "done-idea", "--text=more", "--project", project, "--refine");

        tried.ExitCode.Should().NotBe(0);
        tried.StandardError.Should().Contain("has been accepted").And.NotContain("refine failed");
        (await File.ReadAllBytesAsync(record)).Should().Equal(before);
    }

    [BuiltCliFact]
    public async Task An_unread_note_is_removed_and_a_dry_run_changes_nothing()
    {
        using var loadout = new LoadoutProcess();
        var (project, record) = await Proposed(loadout);
        (await loadout.RunAsync("idea", "add", "--to", "sync", $"--text={Note}", "--project", project, "--no-refine")).ExitCode.Should().Be(0);
        var before = await File.ReadAllBytesAsync(record);

        var tried = await loadout.RunAsync("idea", "note", "remove", "sync", "1", "--project", project, "--dry-run");

        tried.ExitCode.Should().Be(0, tried.StandardError);
        tried.StandardOutput.Should().Contain("Would remove N1 from sync. Nothing was changed.");
        (await File.ReadAllBytesAsync(record)).Should().Equal(before);

        var removed = await loadout.RunAsync("idea", "note", "remove", "sync", "N1", "--project", project, "--json");

        removed.ExitCode.Should().Be(0, removed.StandardError);
        removed.Json().GetProperty("removed").GetString().Should().Be("N1");
        (await File.ReadAllTextAsync(record)).Should().NotContain(Note);
    }

    [BuiltCliFact]
    public async Task A_note_a_round_has_read_is_refused_and_left_as_it_was()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");
        var record = await Write(loadout, project, "read", """
            id: read
            ask: A status page for the home lab.
            notes:
            - id: N1
              text: also on mobile
              by: nigel
              added_utc: 2026-10-19T09:00:00.0000000+00:00
              taken_utc: 2026-10-19T10:00:00.0000000+00:00
            """);
        var before = await File.ReadAllBytesAsync(record);

        var tried = await loadout.RunAsync("idea", "note", "remove", "read", "1", "--project", project);

        tried.ExitCode.Should().NotBe(0);
        tried.StandardError.Should().Contain("read by a round");
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

    private static string GitDirectory()
    {
        var name = OperatingSystem.IsWindows() ? "git.exe" : "git";

        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .First(directory => File.Exists(Path.Combine(directory, name)));
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

using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// A task's title kept to a line, its description kept whole in the detail,
/// and every change of state kept in its history, against real files.
/// <para>
/// Tasks split out of a note dump, and plain ones somebody typed in a hurry,
/// put a paragraph in the title, and every list that showed one was swamped
/// by it. Cutting it on screen lost the end of it, so the cut is made once,
/// where it is written, with nothing lost.
/// </para>
/// </summary>
public sealed class TaskDetailTests : IDisposable
{
    private const string Long =
        "Make idea and task lists in Studio and the CLI quick to scan, because today a "
        + "card shows the whole description and nothing else fits beside it";

    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private async Task<TaskItem> OnlyAsync(string? slug = "website") =>
        (await _space.Tasks.ListAsync(slug)).Value!.Single();

    [Fact]
    public async Task A_long_title_is_cut_at_a_word_and_kept_whole_in_the_detail()
    {
        await _space.ProjectAsync("website");

        var declared = await _space.Tasks.DeclareAsync("website", "scan", TaskState.Open, "nigel", Long);

        declared.Succeeded.Should().BeTrue(declared.Error);

        var task = await OnlyAsync();
        task.Title.Length.Should().BeLessThanOrEqualTo(TaskTitles.Length);
        task.Title.Should().Be("Make idea and task lists in Studio and the CLI quick to scan, because…");
        task.Detail.Should().Be(Long, "the original survives word for word");
        task.History.Single().Reason.Should().Be(TaskTitles.ShortenedReason);
    }

    [Fact]
    public async Task A_title_within_the_limit_is_left_as_it_was()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the login");

        var task = await OnlyAsync();
        task.Title.Should().Be("Fix the login");
        task.Detail.Should().BeEmpty();
        task.History.Single().Reason.Should().BeEmpty();
    }

    [Fact]
    public async Task A_detail_given_with_a_long_title_follows_the_original()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync(
            "website", "scan", TaskState.Open, "nigel", Long, detail: "Start with TaskBoard.svelte.");

        (await OnlyAsync()).Detail.Should().Be(Long + "\n\nStart with TaskBoard.svelte.");
    }

    [Fact]
    public async Task The_same_long_title_given_again_is_not_kept_twice()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync("website", "scan", TaskState.Open, "nigel", Long);
        await _space.Tasks.DeclareAsync("website", "scan", TaskState.Doing, "agent", Long);

        var task = await OnlyAsync();
        task.Detail.Should().Be(Long);
        task.History.Select(one => one.Reason).Should().Equal(TaskTitles.ShortenedReason, string.Empty);
    }

    [Fact]
    public async Task Each_change_of_state_is_kept_and_a_state_said_again_is_not()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the login");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Doing, "agent");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Doing, "agent", note: "halfway");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Done, "nigel");

        var task = await OnlyAsync();
        task.History.Select(one => (one.State, one.By)).Should().Equal(
            (TaskState.Open, "nigel"), (TaskState.Doing, "agent"), (TaskState.Done, "nigel"));
        task.DeclaredBy.Should().Be("nigel", "the latest claim is still where it always was");
        task.History[^1].Utc.Should().Be(task.DeclaredUtc);
    }

    [Fact]
    public async Task A_task_written_before_there_was_a_history_keeps_its_last_claim()
    {
        await _space.ProjectAsync("website");
        var path = TaskPaths.List(_space.Workspace.LocalPath, "website");

        // As an older launcher writes it: no detail and no history.
        await File.WriteAllTextAsync(path, """
            schema_version: 1
            items:
            - id: login
              title: Fix the login
              state: Open
              kind: Task
              declared_by: someone-earlier
              declared_utc: 2026-09-01T10:00:00.0000000+00:00
              note: ''
            """);

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Doing, "agent");

        var task = await OnlyAsync();
        task.History.Select(one => (one.State, one.By)).Should().Equal(
            (TaskState.Open, "someone-earlier"), (TaskState.Doing, "agent"));
        task.History[0].Utc.Should().Be(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_credential_in_the_detail_is_refused_like_one_anywhere_else()
    {
        await _space.ProjectAsync("website");

        var declared = await _space.Tasks.DeclareAsync(
            "website", "deploy", TaskState.Open, "agent", "Deploy",
            detail: "use ghp_" + new string('a', 36));

        declared.Failed.Should().BeTrue();
        declared.Error.Should().NotContain("ghp_");
        (await _space.Tasks.ListAsync("website")).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Shortening_without_apply_says_what_it_would_do_and_writes_nothing()
    {
        await _space.ProjectAsync("website");
        var path = await LegacyAsync();
        var before = await File.ReadAllTextAsync(path);

        var found = await _space.Tasks.ShortenTitlesAsync("website", "nigel", apply: false);

        found.Value!.Single().Should().Be(new TaskShortened("scan", Long, TaskTitles.Shorten(Long)));
        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [Fact]
    public async Task Shortening_keeps_the_original_says_so_and_leaves_the_claim_alone()
    {
        await _space.ProjectAsync("website");
        await LegacyAsync();

        var done = await _space.Tasks.ShortenTitlesAsync("website", "nigel", apply: true);

        done.Value!.Select(one => one.Id).Should().Equal("scan");

        var scan = (await _space.Tasks.ListAsync("website")).Value!.Single(t => t.Id == "scan");
        scan.Title.Should().Be(TaskTitles.Shorten(Long));
        scan.Detail.Should().Be(Long + "\n\nWhat was already in the detail.");
        scan.State.Should().Be(TaskState.Doing);
        scan.DeclaredBy.Should().Be("someone-earlier", "shortening is not a claim about where it stands");
        scan.History.Select(one => (one.State, one.By, one.Reason)).Should().Equal(
            (TaskState.Doing, "someone-earlier", string.Empty),
            (TaskState.Doing, "nigel", TaskTitles.ShortenedReason));

        var short_ = (await _space.Tasks.ListAsync("website")).Value!.Single(t => t.Id == "short");
        short_.History.Should().BeEmpty("a task it had no reason to touch is left as it was");
    }

    [Fact]
    public async Task Shortening_twice_finds_nothing_the_second_time()
    {
        await _space.ProjectAsync("website");
        var path = await LegacyAsync();

        await _space.Tasks.ShortenTitlesAsync("website", "nigel", apply: true);
        var after = await File.ReadAllTextAsync(path);

        var again = await _space.Tasks.ShortenTitlesAsync("website", "nigel", apply: true);

        again.Value!.Should().BeEmpty();
        (await File.ReadAllTextAsync(path)).Should().Be(after, "a list with nothing to shorten is not rewritten");
    }

    [Fact]
    public async Task A_batch_carries_a_detail_and_shortens_a_long_title_as_a_declare_does()
    {
        await _space.ProjectAsync("website");

        var applied = await _space.Tasks.ApplyAsync(
        [
            new TaskOperation(TaskOperation.Add, "website", "scan", Title: Long, By: "agent"),
            new TaskOperation(TaskOperation.Declare, "website", "login", TaskState.Open, "Fix the login", By: "agent", Detail: "It fails after a reboot."),
        ]);

        applied.Value!.Applied.Should().BeTrue();

        var tasks = (await _space.Tasks.ListAsync("website")).Value!;
        tasks.Single(t => t.Id == "scan").Detail.Should().Be(Long);
        tasks.Single(t => t.Id == "scan").Title.Length.Should().BeLessThanOrEqualTo(TaskTitles.Length);
        tasks.Single(t => t.Id == "login").Detail.Should().Be("It fails after a reboot.");
    }

    [Fact]
    public void A_batch_read_from_JSON_carries_its_detail()
    {
        var read = TaskOperations.Parse(
            """[{"op": "declare", "id": "login", "state": "open", "detail": "It fails after a reboot."}]""", "website", "studio");

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Single().Detail.Should().Be("It fails after a reboot.");
    }

    [Fact]
    public async Task Every_list_is_found_the_workspace_wide_one_first()
    {
        await _space.ProjectAsync("website");
        await _space.ProjectAsync("homelab");
        await _space.Tasks.DeclareAsync("website", "a", TaskState.Open, "nigel", "A");
        await _space.Tasks.DeclareAsync(null, "b", TaskState.Open, "nigel", "B");

        TaskPaths.Lists(_space.Workspace.LocalPath).Should().Equal(null, "website");
    }

    private async Task<string> LegacyAsync()
    {
        var path = TaskPaths.List(_space.Workspace.LocalPath, "website");

        await File.WriteAllTextAsync(path, $"""
            schema_version: 1
            items:
            - id: scan
              title: {Long}
              state: Doing
              kind: Task
              declared_by: someone-earlier
              declared_utc: 2026-09-01T10:00:00.0000000+00:00
              note: ''
              detail: What was already in the detail.
            - id: short
              title: Fix the login
              state: Open
              kind: Task
              declared_by: someone-earlier
              declared_utc: 2026-09-01T10:00:00.0000000+00:00
            """);

        return path;
    }
}

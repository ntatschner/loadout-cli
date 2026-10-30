using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Every task and idea carries a type, guessed when nobody gives one, and a
/// person's choice outranks every guess, against real files.
/// </summary>
public sealed class TaskTypeTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private async Task<TaskItem> OneAsync(string id, string? slug = "website") =>
        (await _space.Tasks.ListAsync(slug)).Value!.Single(task => task.Id == id);

    [Fact]
    public async Task A_new_task_given_no_type_has_one_guessed_from_its_words()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the broken login");

        var task = await OneAsync("login");
        task.Type.Should().Be(TaskType.BugFix);
        task.TypeBy.Should().Be(TaskTypeSource.Rule);
    }

    [Fact]
    public async Task A_new_task_given_a_kind_and_no_type_is_still_guessed()
    {
        // As Studio's add form sends it: a kind, and no type.
        await _space.Tasks.DeclareAsync(null, "cats", TaskState.Open, "nigel", "Track what the cats eat", kind: TaskKind.Idea);

        var idea = await OneAsync("cats", null);
        idea.Type.Should().Be(TaskType.NewProject, "an idea on the workspace-wide list is guessed by where it was captured");
        idea.TypeBy.Should().Be(TaskTypeSource.Rule);
    }

    [Fact]
    public async Task The_guess_reads_the_detail_as_well_as_the_title()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync(
            "website", "pages", TaskState.Open, "nigel", "Pages", detail: "Write the guide and the README for it.");

        (await OneAsync("pages")).Type.Should().Be(TaskType.Docs);
    }

    [Fact]
    public async Task A_type_given_is_a_persons_choice()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync(
            "website", "login", TaskState.Open, "nigel", "Fix the broken login", type: TaskType.Maintenance);

        var task = await OneAsync("login");
        task.Type.Should().Be(TaskType.Maintenance);
        task.TypeBy.Should().Be(TaskTypeSource.Person);
    }

    [Fact]
    public async Task Declaring_a_state_later_leaves_the_type_alone()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the broken login");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Doing, "agent", "Add the docs for logging in");

        var task = await OneAsync("login");
        task.Type.Should().Be(TaskType.BugFix, "only a new task is guessed; a new title is no reason to guess again");
        task.TypeBy.Should().Be(TaskTypeSource.Rule);
    }

    [Fact]
    public async Task The_agent_may_revise_a_rules_guess()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the broken login");
        await _space.Tasks.DeclareAsync(
            "website", "login", TaskState.Open, "agent", type: TaskType.Feature, typeBy: TaskTypeSource.Agent);

        var task = await OneAsync("login");
        task.Type.Should().Be(TaskType.Feature);
        task.TypeBy.Should().Be(TaskTypeSource.Agent);
    }

    [Fact]
    public async Task No_guess_replaces_a_persons_choice()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync(
            "website", "login", TaskState.Open, "nigel", "Fix the broken login", type: TaskType.Release);
        await _space.Tasks.DeclareAsync(
            "website", "login", TaskState.Doing, "agent", type: TaskType.Feature, typeBy: TaskTypeSource.Agent);

        var task = await OneAsync("login");
        task.Type.Should().Be(TaskType.Release);
        task.TypeBy.Should().Be(TaskTypeSource.Person);
        task.State.Should().Be(TaskState.Doing, "the rest of what the agent said is still recorded");
    }

    [Fact]
    public async Task A_person_changing_the_type_is_kept_in_the_history()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the broken login");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", type: TaskType.Docs);

        var task = await OneAsync("login");
        task.History.Select(one => one.Reason).Should().Equal(string.Empty, TaskTypes.ChangedReason(TaskType.Docs));
    }

    [Fact]
    public async Task The_type_is_written_to_the_file_and_read_back()
    {
        await _space.ProjectAsync("website");

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Ship it", type: TaskType.Release);

        var text = await File.ReadAllTextAsync(TaskPaths.List(_space.Workspace.LocalPath, "website"));
        text.Should().Contain("type: Release").And.Contain("type_by: Person");
    }

    [Fact]
    public async Task An_idea_is_typed_from_everything_that_was_asked()
    {
        await _space.ProjectAsync("website");

        await _space.Ideas.CaptureAsync(
            "website",
            "Status page. The deploy pipeline should run on every pull request and the CI legs report here",
            "nigel",
            "status-page");

        var idea = await OneAsync("status-page");
        idea.Kind.Should().Be(TaskKind.Idea);
        idea.Type.Should().Be(TaskType.CiCd, "the words past the title say what it is about");
        idea.TypeBy.Should().Be(TaskTypeSource.Rule);
    }

    [Fact]
    public async Task An_idea_dropped_on_the_workspace_wide_list_is_a_new_project()
    {
        await _space.Ideas.CaptureAsync(null, "Something to track what the cats eat", "nigel", "cats");

        (await OneAsync("cats", null)).Type.Should().Be(TaskType.NewProject);
    }

    [Fact]
    public async Task Classifying_without_apply_says_what_it_would_do_and_writes_nothing()
    {
        await _space.ProjectAsync("website");
        var path = await LegacyAsync();
        var before = await File.ReadAllTextAsync(path);

        var found = await _space.Tasks.ClassifyAsync("website", "nigel", apply: false);

        found.Value!.Should().Equal(new TaskClassified("login", "Fix the broken login", TaskType.BugFix));
        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [Fact]
    public async Task Classifying_records_a_guess_says_so_and_leaves_the_claim_alone()
    {
        await _space.ProjectAsync("website");
        await LegacyAsync();

        await _space.Tasks.ClassifyAsync("website", "nigel", apply: true);

        var login = await OneAsync("login");
        login.Type.Should().Be(TaskType.BugFix);
        login.TypeBy.Should().Be(TaskTypeSource.Rule, "running a command is not a person choosing each type");
        login.DeclaredBy.Should().Be("someone-earlier", "classifying is not a claim about where it stands");
        login.History.Select(one => (one.By, one.Reason)).Should().Equal(
            ("someone-earlier", string.Empty),
            ("nigel", TaskTypes.GuessedReason(TaskType.BugFix)));

        var chosen = await OneAsync("notes");
        chosen.Type.Should().Be(TaskType.Release, "a task that already has a type is left as it was");
        chosen.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Classifying_twice_finds_nothing_the_second_time()
    {
        await _space.ProjectAsync("website");
        var path = await LegacyAsync();

        await _space.Tasks.ClassifyAsync("website", "nigel", apply: true);
        var after = await File.ReadAllTextAsync(path);

        var again = await _space.Tasks.ClassifyAsync("website", "nigel", apply: true);

        again.Value!.Should().BeEmpty();
        (await File.ReadAllTextAsync(path)).Should().Be(after, "a list with nothing to type is not rewritten");
    }

    [Fact]
    public async Task A_batch_takes_a_type_and_refuses_one_that_is_not()
    {
        var parsed = TaskOperations.Parse(
            """[{"op":"declare","id":"a","state":"open","type":"ci-cd"},{"op":"declare","id":"b","state":"open","type":"bug fix"}]""",
            "website",
            "nigel");

        parsed.Value!.Select(one => one.Type).Should().Equal(TaskType.CiCd, TaskType.BugFix);

        var refused = TaskOperations.Parse("""[{"op":"declare","id":"a","state":"open","type":"chore"}]""", "website", "nigel");

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain("type 'chore'").And.Contain("bug-fix");
    }

    /// <summary>A list as a launcher from before types wrote it, beside one task typed since.</summary>
    private async Task<string> LegacyAsync()
    {
        var path = TaskPaths.List(_space.Workspace.LocalPath, "website");

        await File.WriteAllTextAsync(path, """
            schema_version: 1
            items:
            - id: login
              title: Fix the broken login
              state: Open
              kind: Task
              declared_by: someone-earlier
              declared_utc: 2026-09-01T10:00:00.0000000+00:00
              note: ''
            - id: notes
              title: Fix the release notes
              state: Open
              kind: Task
              type: Release
              type_by: Person
              declared_by: nigel
              declared_utc: 2026-09-02T10:00:00.0000000+00:00
              note: ''
            """);

        return path;
    }
}

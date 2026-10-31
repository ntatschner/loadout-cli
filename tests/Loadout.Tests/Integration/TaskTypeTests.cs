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

    [Fact]
    public async Task A_rounds_type_revises_a_guess_and_says_so()
    {
        await _space.ProjectAsync("website");
        await _space.Ideas.CaptureAsync("website", "Status page for the hosts", "nigel", "status");
        var place = new Core.Ideas.IdeaPlace("website", "status");

        (await _space.Ideas.RecordReplyAsync(place, Reply("bug-fix"))).Succeeded.Should().BeTrue();

        var idea = await OneAsync("status");
        idea.Type.Should().Be(TaskType.BugFix);
        idea.TypeBy.Should().Be(TaskTypeSource.Agent);
        idea.DeclaredBy.Should().Be("nigel", "revising a type is not a claim about where the work stands");
        idea.History[^1].Reason.Should().Be(TaskTypes.Reason(TaskType.BugFix, TaskTypeSource.Agent));
    }

    [Fact]
    public async Task A_rounds_type_leaves_a_persons_choice_alone()
    {
        await _space.ProjectAsync("website");
        await _space.Ideas.CaptureAsync("website", "Status page for the hosts", "nigel", "status");
        var place = new Core.Ideas.IdeaPlace("website", "status");
        await _space.Ideas.RetypeAsync(place, TaskType.Docs, "nigel");

        (await _space.Ideas.RecordReplyAsync(place, Reply("bug-fix"))).Succeeded.Should().BeTrue("the round is kept all the same");

        var idea = await OneAsync("status");
        idea.Type.Should().Be(TaskType.Docs);
        idea.TypeBy.Should().Be(TaskTypeSource.Person);
    }

    [Fact]
    public async Task A_round_with_no_type_or_one_that_is_not_changes_nothing()
    {
        await _space.ProjectAsync("website");
        await _space.Ideas.CaptureAsync("website", "Status page for the hosts", "nigel", "status");
        var place = new Core.Ideas.IdeaPlace("website", "status");
        var before = await OneAsync("status");

        await _space.Ideas.RecordReplyAsync(place, Reply(string.Empty));
        await _space.Ideas.RecordReplyAsync(place, Reply("chore"));

        var idea = await OneAsync("status");
        idea.Type.Should().Be(before.Type);
        idea.TypeBy.Should().Be(TaskTypeSource.Rule);
    }

    [Fact]
    public async Task Handing_the_type_back_puts_the_rules_guess_in_its_place()
    {
        await _space.ProjectAsync("website");
        await _space.Ideas.CaptureAsync("website", "The login is broken on Safari", "nigel", "login");
        var place = new Core.Ideas.IdeaPlace("website", "login");
        await _space.Ideas.RetypeAsync(place, TaskType.Docs, "nigel");

        var handed = await _space.Ideas.RetypeAsync(place, null, "nigel");

        handed.Succeeded.Should().BeTrue(handed.Error);
        handed.Value!.Type.Should().Be(TaskType.BugFix, "the guess reads what was asked");
        handed.Value.TypeBy.Should().Be(TaskTypeSource.Rule, "a guess the next round may revise");
        handed.Value.History.Select(one => one.Reason).Should().EndWith(
            [TaskTypes.ChangedReason(TaskType.Docs), TaskTypes.Reason(TaskType.BugFix, TaskTypeSource.Rule)]);

        await _space.Ideas.RecordReplyAsync(place, Reply("feature"));

        (await OneAsync("login")).Type.Should().Be(TaskType.Feature, "the next round decides it");
    }

    [Fact]
    public async Task Choosing_the_type_already_chosen_records_nothing_more()
    {
        await _space.ProjectAsync("website");
        await _space.Ideas.CaptureAsync("website", "Status page for the hosts", "nigel", "status");
        var place = new Core.Ideas.IdeaPlace("website", "status");

        await _space.Ideas.RetypeAsync(place, TaskType.Docs, "nigel");
        var path = TaskPaths.List(_space.Workspace.LocalPath, "website");
        var once = await File.ReadAllTextAsync(path);

        await _space.Ideas.RetypeAsync(place, TaskType.Docs, "nigel");

        (await File.ReadAllTextAsync(path)).Should().Be(once);
    }

    [Fact]
    public async Task A_batch_cannot_retype_around_the_lock()
    {
        await _space.ProjectAsync("website");

        var done = await _space.Tasks.ApplyAsync(
            [new TaskOperation(TaskOperation.Retype, "website", "login", Type: TaskType.Docs, TypeBy: TaskTypeSource.Agent)]);

        done.Value!.Outcomes.Single().Applied.Should().BeFalse();
    }

    [Fact]
    public void The_prompt_asks_for_a_type_unless_the_person_chose_one()
    {
        var record = new Models.Ideas.IdeaRecord { Id = "status", Ask = "A status page" };

        var guessed = Core.Ideas.IdeaWork.Prompt(
            record, [], null, new TaskItem { Type = TaskType.Feature, TypeBy = TaskTypeSource.Rule });
        var chosen = Core.Ideas.IdeaWork.Prompt(
            record, [], null, new TaskItem { Type = TaskType.Docs, TypeBy = TaskTypeSource.Person });

        guessed.Should().Contain("typed feature for now").And.Contain("Set type to whichever of new-project");
        chosen.Should().Contain("They chose docs as its type themselves, so leave type out");
        chosen.Should().NotContain("Set type to");
    }

    [Fact]
    public void The_schema_offers_exactly_the_seven_types_and_does_not_require_one()
    {
        using var schema = System.Text.Json.JsonDocument.Parse(Models.Ideas.IdeaSchema.Version3);
        var root = schema.RootElement;

        root.GetProperty("properties").GetProperty("type").GetProperty("enum").EnumerateArray()
            .Select(one => one.GetString()).Should().Equal(TaskTypes.Names);
        root.GetProperty("required").EnumerateArray().Select(one => one.GetString()).Should().NotContain("type");
    }

    [Fact]
    public async Task Labels_are_added_once_whatever_their_case_and_taken_off_whatever_their_case()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the login");

        await _space.Tasks.LabelAsync("website", "login", ["Homelab", "urgent"], []);
        await _space.Tasks.LabelAsync("website", "login", ["homelab", "someday"], []);
        var taken = await _space.Tasks.LabelAsync("website", "login", [], ["URGENT"]);

        taken.Value!.Labels.Should().Equal("Homelab", "someday");
        (await OneAsync("login")).Labels.Should().Equal("Homelab", "someday");
    }

    [Fact]
    public async Task Labelling_leaves_the_claim_and_the_history_alone()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the login");

        await _space.Tasks.LabelAsync("website", "login", ["homelab"], []);

        var task = await OneAsync("login");
        task.History.Should().ContainSingle("a label is not a claim about the work");
        task.DeclaredBy.Should().Be("nigel");
    }

    [Theory]
    [InlineData("", "needs a word")]
    [InlineData("a label far too long to be a word or two", "at most 32")]
    [InlineData("one, two", "comma")]
    [InlineData("bug fix", "is a type")]
    public async Task A_label_that_is_not_one_is_refused_with_the_reason(string label, string reason)
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the login");

        var refused = await _space.Tasks.LabelAsync("website", "login", [label], []);

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain(reason);
        (await OneAsync("login")).Labels.Should().BeEmpty();
    }

    [Fact]
    public async Task A_batch_cannot_label()
    {
        await _space.ProjectAsync("website");

        var done = await _space.Tasks.ApplyAsync(
            [new TaskOperation(TaskOperation.Label, "website", "login", Labels: ["homelab"])]);

        done.Value!.Outcomes.Single().Applied.Should().BeFalse();
    }

    [Fact]
    public async Task Renaming_a_label_keeps_its_place_and_does_not_double_one_already_there()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "a", TaskState.Open, "nigel", "One");
        await _space.Tasks.DeclareAsync("website", "b", TaskState.Open, "nigel", "Two");
        await _space.Tasks.DeclareAsync("website", "c", TaskState.Open, "nigel", "Three");
        await _space.Tasks.LabelAsync("website", "a", ["first", "homelab", "last"], []);
        await _space.Tasks.LabelAsync("website", "b", ["Homelab", "home-lab"], []);

        var preview = await _space.Tasks.RelabelAsync("website", "HOMELAB", "home-lab", apply: false);
        preview.Value!.Should().Equal("a", "b");
        (await OneAsync("a")).Labels.Should().Equal("first", "homelab", "last");

        await _space.Tasks.RelabelAsync("website", "HOMELAB", "home-lab", apply: true);

        (await OneAsync("a")).Labels.Should().Equal("first", "home-lab", "last");
        (await OneAsync("b")).Labels.Should().Equal("home-lab");
        (await OneAsync("c")).Labels.Should().BeEmpty();
    }

    [Fact]
    public async Task Removing_a_label_takes_it_off_every_task_on_the_list()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "a", TaskState.Open, "nigel", "One");
        await _space.Tasks.DeclareAsync("website", "b", TaskState.Open, "nigel", "Two");
        await _space.Tasks.LabelAsync("website", "a", ["someday", "homelab"], []);
        await _space.Tasks.LabelAsync("website", "b", ["someday"], []);

        var removed = await _space.Tasks.RelabelAsync("website", "someday", null, apply: true);

        removed.Value!.Should().Equal("a", "b");
        (await OneAsync("a")).Labels.Should().Equal("homelab");
        (await OneAsync("b")).Labels.Should().BeEmpty();
    }

    [Fact]
    public void A_list_filters_on_a_label()
    {
        var labelled = new TaskItem { Id = "a", Labels = ["homelab"] };
        var not = new TaskItem { Id = "b" };
        var query = Core.Lists.ListQuery.Parse("label:homelab").Value!;

        TaskFields.All.Apply(query, [labelled, not], DateTimeOffset.UnixEpoch).Value!.Select(one => one.Id).Should().Equal("a");
    }

    private static Core.Ideas.IdeaReply Reply(string type) => new()
    {
        Contract = Models.Ideas.IdeaSchema.Version,
        Understanding = "A page.",
        Type = type,
        Questions = [new Core.Ideas.IdeaReply.ReplyQuestion { Question = "Public?", Why = "Size." }],
    };

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

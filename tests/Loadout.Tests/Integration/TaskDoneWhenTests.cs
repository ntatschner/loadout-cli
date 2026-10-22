using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Instructions;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Models.Configuration;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// A task's done-when: kept on the task, changed as a whole, and holding a run
/// for the task to it.
/// </summary>
public sealed class TaskDoneWhenTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private async Task<TaskItem> ReadAsync(string id) =>
        (await _space.Tasks.ListAsync("website")).Value!.Single(one => one.Id == id);

    [Fact]
    public async Task Done_when_given_is_kept_trimmed_with_blank_ones_dropped_and_survives_a_reread()
    {
        await _space.ProjectAsync("website");

        (await _space.Tasks.DeclareAsync(
            "website", "login", TaskState.Open, "nigel", "Fix the login",
            doneWhen: [" the suite passes ", "", "login works with a, b and c"])).Succeeded.Should().BeTrue();

        (await ReadAsync("login")).DoneWhen.Should().Equal("the suite passes", "login works with a, b and c");
    }

    [Fact]
    public async Task Declaring_without_done_when_leaves_it_and_an_empty_list_clears_it()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the login", doneWhen: ["it works"]);

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Doing, "nigel");
        (await ReadAsync("login")).DoneWhen.Should().Equal("it works");

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Doing, "nigel", doneWhen: ["it works", "it is fast"]);
        (await ReadAsync("login")).DoneWhen.Should().Equal("it works", "it is fast");

        await _space.Tasks.DeclareAsync("website", "login", TaskState.Doing, "nigel", doneWhen: []);
        (await ReadAsync("login")).DoneWhen.Should().BeEmpty();
    }

    [Fact]
    public async Task A_batch_takes_done_when_as_a_list_and_writes_it_back_out()
    {
        await _space.ProjectAsync("website");

        var parsed = TaskOperations.Parse(
            """[{"op":"add","id":"login","title":"Fix the login","doneWhen":["the suite passes","it works, really"]}]""",
            "website",
            "nigel");

        parsed.Succeeded.Should().BeTrue(parsed.Error);

        var batch = (await _space.Tasks.ApplyAsync(parsed.Value!)).Value!;

        batch.Applied.Should().BeTrue();
        (await ReadAsync("login")).DoneWhen.Should().Equal("the suite passes", "it works, really");
        System.Text.Json.JsonSerializer.Serialize(TaskOperations.Json(batch.Outcomes[0].Task!))
            .Should().Contain("\"doneWhen\":[\"the suite passes\",\"it works, really\"]");
    }

    [Fact]
    public void A_batch_refuses_done_when_holding_anything_but_text()
    {
        TaskOperations.Parse("""[{"op":"add","title":"x","doneWhen":["ok",3]}]""", "website", "nigel")
            .Error.Should().Contain("'doneWhen' with something other than text");
    }

    [Fact]
    public async Task A_credential_in_done_when_is_refused_like_one_anywhere_else()
    {
        await _space.ProjectAsync("website");

        var declared = await _space.Tasks.DeclareAsync(
            "website", "login", TaskState.Open, "nigel", "Fix the login",
            doneWhen: ["the key ghp_" + new string('a', 36) + " works"]);

        declared.Failed.Should().BeTrue();
        declared.Error.Should().Contain("looks like it contains a credential");
    }

    [Fact]
    public void A_run_for_a_task_is_held_to_the_tasks_done_when_after_its_own_and_to_each_once()
    {
        var request = TeamRunCommand.Requesting(
            "website",
            new TeamDefinition { Name = "builders" },
            SpecialistCatalogue.Empty,
            new TeamRunCommand.Settings { Goal = "Fix the login", DoneWhen = ["The suite passes"] },
            "autonomous",
            TeamCeiling.Nothing,
            new MachineConfig(),
            projectTeams: null,
            taskDoneWhen: ["the suite passes", "login works"]);

        request.Criteria.Should().Equal("The suite passes", "login works");
    }
}

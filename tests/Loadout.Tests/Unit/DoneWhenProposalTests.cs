using FluentAssertions;
using Loadout.Agents.Ideas;
using Loadout.Agents.Tasks;
using Loadout.Cli.Commands;
using Loadout.Core.Tasks;
using Loadout.Models.Agents;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Asking an agent for a task's done-when: what it is told, what is accepted
/// back, and that nothing is recorded by the asking.
/// </summary>
public sealed class DoneWhenProposalTests : IDisposable
{
    private const string Good =
        """{"contract":"donewhen/1","doneWhen":["loadout team queue add accepts the task"," The login test passes "],"why":"From the plan's two layers."}""";

    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private DoneWhenProposer Proposer(ScriptedDetachedLauncher launcher, string checkout) =>
        new(_space.Tasks, _space.Ideas, new FakeProjects("website", checkout), _space.Workspace, launcher, _space.Paths);

    [Fact]
    public void The_prompt_carries_the_task_what_it_already_has_and_its_plan()
    {
        var task = new TaskItem
        {
            Id = "login",
            Title = "Fix the login",
            Note = "After the cookie change.",
            Detail = "Users are logged out after a minute.",
            DoneWhen = ["it works"],
        };

        var prompt = DoneWhenWork.Prompt(task, "website", "# Plan\n\nKeep the session cookie for a day.");

        prompt.Should().Contain("the website project's task list")
            .And.Contain("Title: Fix the login")
            .And.Contain("Note: After the cookie change.")
            .And.Contain("Users are logged out after a minute.")
            .And.Contain("which your proposal would replace:")
            .And.Contain("- it works")
            .And.Contain("Keep the session cookie for a day.")
            .And.Contain("not just 'the tests pass'")
            .And.Contain("donewhen/1");
    }

    [Fact]
    public void A_very_long_plan_is_cut_and_says_so()
    {
        var prompt = DoneWhenWork.Prompt(new TaskItem { Id = "a", Title = "A" }, null, new string('x', 20_000));

        prompt.Should().Contain("[the rest of the plan is left out]").And.Contain("the workspace-wide task list");
        prompt.Length.Should().BeLessThan(15_000);
    }

    [Fact]
    public void A_good_answer_is_read_trimmed_and_without_repeats()
    {
        var read = DoneWhenWork.Read(
            """{"contract":"donewhen/1","doneWhen":[" one ","two","ONE",""],"why":" because "}""");

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.DoneWhen.Should().Equal("one", "two");
        read.Value.Why.Should().Be("because");
    }

    [Theory]
    [InlineData("""{"contract":"idea/2","doneWhen":["a","b"],"why":""}""", "donewhen/1 contract")]
    [InlineData("""{"contract":"donewhen/1","why":""}""", "no doneWhen list")]
    [InlineData("""{"contract":"donewhen/1","doneWhen":["only one"],"why":""}""", "between 2 and 5")]
    [InlineData("""{"contract":"donewhen/1","doneWhen":["a","b","c","d","e","f"],"why":""}""", "between 2 and 5")]
    [InlineData("not json", "not JSON")]
    [InlineData("", "no answer")]
    public void An_answer_that_cannot_be_used_is_refused_with_why(string json, string said)
    {
        DoneWhenWork.Read(json).Error.Should().Contain(said);
    }

    [Fact]
    public void A_condition_that_is_a_paragraph_is_refused()
    {
        var json = $$"""{"contract":"donewhen/1","doneWhen":["short","{{new string('x', 301)}}"],"why":""}""";

        DoneWhenWork.Read(json).Error.Should().Contain("one sentence");
    }

    [Fact]
    public async Task A_proposal_is_asked_in_the_projects_checkout_with_read_only_tools_and_records_nothing()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the login", detail: "Logged out after a minute.");
        var checkout = _space.Scratch("website");
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good));

        var proposed = await Proposer(launcher, checkout).ProposeAsync(new DoneWhenRequest("website", "login"));

        proposed.Succeeded.Should().BeTrue(proposed.Error);
        proposed.Value!.Proposal!.DoneWhen.Should().Equal("loadout team queue add accepts the task", "The login test passes");
        proposed.Value.CostUsd.Should().BeGreaterThan(0);

        launcher.Request!.WorkingDirectory.Should().Be(checkout);
        launcher.Options!.AllowedTools.Should().Equal(IdeaRefiner.Allowed);
        launcher.Options.DeniedTools.Should().Contain(["Write", "Edit", "Bash"]);
        launcher.Options.OutputSchemaJson.Should().Be(DoneWhenSchema.Version1);
        launcher.Pipe!.Written.ToString().Should().Contain("Logged out after a minute.");

        (await _space.Tasks.ListAsync("website")).Value!.Single().DoneWhen.Should().BeEmpty("asking records nothing");
    }

    [Fact]
    public async Task An_answer_in_the_wrong_shape_is_asked_for_once_more_then_refused()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the login");
        var bad = ScriptedDetachedLauncher.Result("""{"contract":"donewhen/1","doneWhen":["one"],"why":""}""");
        var launcher = new ScriptedDetachedLauncher(bad, bad);

        var proposed = await Proposer(launcher, _space.Scratch("website")).ProposeAsync(new DoneWhenRequest("website", "login"));

        proposed.Error.Should().Contain("between 2 and 5");
        launcher.Pipe!.Written.ToString().Should().Contain("Give your answer again");
    }

    [Theory]
    [InlineData("ghost", "There is no task 'ghost'")]
    [InlineData("thought", "is an idea. Refine it")]
    [InlineData("shipped", "is done, so there is no finish line left to set")]
    public async Task A_task_that_cannot_have_one_is_refused_before_any_agent_starts(string id, string said)
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "thought", TaskState.Open, "nigel", "A thought", kind: TaskKind.Idea);
        await _space.Tasks.DeclareAsync("website", "shipped", TaskState.Done, "nigel", "Shipped");
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good));

        var proposed = await Proposer(launcher, _space.Scratch("website")).ProposeAsync(new DoneWhenRequest("website", id));

        proposed.Error.Should().Contain(said);
        launcher.Started.Should().Be(0);
    }

    [Fact]
    public async Task A_dry_run_asks_nothing()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "login", TaskState.Open, "nigel", "Fix the login");
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Good));

        var proposed = await Proposer(launcher, _space.Scratch("website")).ProposeAsync(new DoneWhenRequest("website", "login", DryRun: true));

        proposed.Value!.Proposal.Should().BeNull();
        proposed.Value.CostUsd.Should().Be(0);
    }

    [Fact]
    public void The_command_to_accept_a_proposal_as_it_stands_quotes_each_condition()
    {
        TaskProposeDoneWhenCommand.Accepting("login", "open", "website", ["the \"login\" test passes", "it is fast"])
            .Should().Be("loadout task declare login open --done-when \"the 'login' test passes\" --done-when \"it is fast\" --project website");

        TaskProposeDoneWhenCommand.Accepting("a", "blocked", null, ["x", "y"]).Should().EndWith("--global");
    }
}

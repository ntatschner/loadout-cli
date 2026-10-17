using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Cli.Commands;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// That each kind of stop reaches the page as its own kind, through the
/// console a daemon-watched run asks with.
/// </summary>
/// <remarks>
/// <para>
/// The page draws a different Gate for each. Readings and the budget went out
/// as "question", the proposed done-when as "brief" and a merge as "confirm",
/// so a page could only tell them apart by the words - and offered "Think
/// again" on the budget, where the runner read it as a figure that was not one
/// and ended the run.
/// </para>
/// <para>
/// Through the wrapper every run puts round its console, because an overload
/// the wrapper did not forward would fall to the interface's default and ask
/// the plain kind: the trap this codebase has fallen into with each of the
/// wrapper's other methods.
/// </para>
/// </remarks>
public sealed class GateKindTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "loadout-kind-" + Guid.NewGuid().ToString("N"));

    private readonly OneAtATime _console;

    public GateKindTests()
    {
        Directory.CreateDirectory(_directory);

        var inner = new DashboardTeamConsole(TimeProvider.System, _ => { });

        inner.Starting(_directory);

        _console = new OneAtATime(inner);
    }

    public void Dispose()
    {
        _console.Dispose();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task The_budget_is_asked_as_the_budget_with_no_think_again()
    {
        var asking = TeamRunner.RaiseAsync(_console, 25.10m, 25m, CancellationToken.None);

        var pending = await WrittenAsync();

        pending.Kind.Should().Be(GateKind.Budget);
        pending.Choices.Should().NotContain(TeamRunner.ThinkAgain, "the lead never asked it, so it has nothing to think again about");
        pending.Choices.Should().Contain(TeamRunner.TakeTheCapOff).And.Contain("Stop the run");

        // Answered as it always was: the option is the answer.
        await NodePermissions.AnswerAsync(_directory, pending.Id, new AskAnswer(true, "yes", Chosen: "Raise it to $38"));

        (await asking).Usd.Should().Be(38);
    }

    [Fact]
    public async Task The_readings_are_asked_as_the_readings_and_can_be_sent_back()
    {
        var question = TeamRunner.ReadingsQuestion(new Dictionary<string, string> { ["the tests pass"] = "the suite is green" });

        var asking = ((ITeamConsole)_console).DecideAsync(question, GateKind.Readings, null);

        var pending = await WrittenAsync();

        pending.Kind.Should().Be(GateKind.Readings);
        pending.Choices.Should().Equal(TeamRunner.AcceptReadings, TeamRunner.ThinkAgain, "Stop the run");

        await NodePermissions.AnswerAsync(_directory, pending.Id, new AskAnswer(true, "yes", Chosen: TeamRunner.ThinkAgain));

        (await asking).Should().Be(TeamRunner.ThinkAgain);
    }

    [Fact]
    public async Task The_proposed_done_when_is_asked_as_itself_and_not_as_a_brief()
    {
        var asking = ((ITeamConsole)_console).ReviseAsync(
            "What this run will be judged on.", "the tests pass", GateKind.DoneWhen);

        var pending = await WrittenAsync();

        pending.Kind.Should().Be(GateKind.DoneWhen);
        pending.Recommendation.Should().Be("the tests pass");

        await NodePermissions.AnswerAsync(
            _directory, pending.Id, new AskAnswer(true, "yes", Chosen: "yes", Instead: "the suite passes"));

        (await asking).Should().Be("the suite passes");
    }

    [Fact]
    public async Task A_merge_is_asked_as_a_merge()
    {
        var asking = ((ITeamConsole)_console).ConfirmAsync("Merge teams-r-implementer into main", GateKind.Merge);

        var pending = await WrittenAsync();

        pending.Kind.Should().Be(GateKind.Merge);
        pending.Choices.Should().Equal("yes", "no");

        await NodePermissions.AnswerAsync(_directory, pending.Id, new AskAnswer(false, "no", Chosen: "no"));

        (await asking).Should().BeFalse();
    }

    /// <remarks>
    /// The calls that say no kind keep the kinds they always had, so every
    /// console and page written before this behaves as it did.
    /// </remarks>
    [Fact]
    public async Task What_says_no_kind_keeps_the_kind_it_had()
    {
        var confirming = _console.ConfirmAsync("Brief the lead");
        var confirm = await WrittenAsync();
        await NodePermissions.AnswerAsync(_directory, confirm.Id, new AskAnswer(true, "yes", Chosen: "yes"));
        await confirming;

        var revising = _console.ReviseAsync("Brief implementer", "do it");
        var brief = await WrittenAsync();
        await NodePermissions.AnswerAsync(_directory, brief.Id, new AskAnswer(true, "yes", Chosen: "yes"));
        await revising;

        var deciding = _console.DecideAsync(new ReportQuestion("Which flag?", ["--since", "--from"], "--since"));
        var question = await WrittenAsync();
        await NodePermissions.AnswerAsync(_directory, question.Id, new AskAnswer(true, "yes", Chosen: "--since"));
        await deciding;

        new[] { confirm.Kind, brief.Kind, question.Kind }
            .Should().Equal(GateKind.Confirm, GateKind.Brief, GateKind.Question);

        question.Choices.Should().Contain(TeamRunner.ThinkAgain);
    }

    private async Task<PendingAsk> WrittenAsync()
    {
        using var give = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (true)
        {
            if (NodePermissions.Pending(_directory).FirstOrDefault() is { } pending)
            {
                return pending;
            }

            await Task.Delay(20, give.Token);
        }
    }
}

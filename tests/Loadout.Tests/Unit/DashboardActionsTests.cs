using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What ending a run from the page types, and what the page is told when the
/// command refuses.
/// </summary>
/// <remarks>
/// Field by field, because a confirmation the page sends and the mapping
/// drops arrives as nothing: the command refuses a run that may still be
/// going, and the page asks again for ever. The contract test proves the
/// parser takes the line; this proves the line carries what was asked.
/// </remarks>
public sealed class DashboardActionsTests
{
    private const string Run = "20261023-1740-2677";

    private static string Typed(RunAction action)
    {
        var (command, arguments) = DashboardActions.Maps(action);

        return string.Join(' ', new[] { command }.Concat(arguments));
    }

    [Fact]
    public void Close_maps_to_team_runs_close()
    {
        Typed(new RunAction(Run, "close")).Should().Be($"team runs close {Run}");
    }

    [Fact]
    public void Confirmed_close_passes_yes()
    {
        Typed(new RunAction(Run, "close", Confirmed: true)).Should().Be($"team runs close {Run} --yes");
    }

    [Theory]
    [InlineData(ExitCode.PolicyViolation, "Something is still driving that run, so it was not ended.")]
    [InlineData(ExitCode.InvalidArguments, "That run may still be going. Confirm to end it.")]
    public async Task Close_refusal_live_run_is_worded(ExitCode code, string said)
    {
        var refused = await DashboardActions.RanAsync(
            new Refusing(code), TimeProvider.System, new RunAction(Run, "close"), Quiet(), CancellationToken.None);

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Be(said);
    }

    [Fact]
    public async Task Confirmed_close_refused_as_invalid_says_the_run_has_ended_or_is_not_one()
    {
        var refused = await DashboardActions.RanAsync(
            new Refusing(ExitCode.InvalidArguments), TimeProvider.System, new RunAction(Run, "close", Confirmed: true), Quiet(), CancellationToken.None);

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain("already ended")
            .And.Contain("not a run")
            .And.Contain("terminal serving this page")
            .And.NotContain("Confirm", "it was confirmed, and asking again would loop");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Close_refused_by_a_general_failure_names_the_owner_file_and_the_journal(bool confirmed)
    {
        var refused = await DashboardActions.RanAsync(
            new Refusing(ExitCode.GeneralFailure), TimeProvider.System, new RunAction(Run, "close", Confirmed: confirmed), Quiet(), CancellationToken.None);

        refused.Error.Should().Contain("owner file could not be read")
            .And.Contain("journal could not be written");
    }

    [Fact]
    public void Pick_up_with_answers_passes_one_joined_answer_each_in_order()
    {
        Typed(new RunAction(Run, "pickup", Answers: ["- by hand", "yes"]))
            .Should().Be($"team resume {Run} --answer=- by hand --answer=yes");
    }

    [Fact]
    public void Pick_up_without_answers_says_so()
    {
        Typed(new RunAction(Run, "pickup", WithoutAnswers: true)).Should().Be($"team resume {Run} --without-answers");
    }

    [Fact]
    public void The_page_s_answers_and_its_choice_to_carry_on_without_are_read_by_name()
    {
        using var body = System.Text.Json.JsonDocument.Parse("""{"answers":["By hand","Yes"],"withoutAnswers":true,"budget":"40"}""");

        var action = DashboardServer.ActionFrom(Run, "pickup", body.RootElement);

        action.Answers.Should().Equal("By hand", "Yes");
        action.WithoutAnswers.Should().BeTrue();
        action.Budget.Should().Be("40");
    }

    [Fact]
    public async Task A_pick_up_refused_for_unanswered_questions_says_to_answer_them()
    {
        var refused = await DashboardActions.RanAsync(
            new Refusing(ExitCode.TerminalRequired), TimeProvider.System, new RunAction(Run, "pickup"), Quiet(), CancellationToken.None);

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain("questions only you can answer").And.Contain("carry on without answering");
    }

    private static CommandOutput Quiet() =>
        new(AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null),
            Interactive = InteractionSupport.No,
        }), new GlobalSettings());

    private sealed class Refusing(ExitCode code) : ICommandCatalogue
    {
        public IReadOnlyList<CatalogueEntry> Commands => [];

        public Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default) =>
            Task.FromResult((int)code);
    }
}

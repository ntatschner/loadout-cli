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
    public async Task Close_refused_for_an_unreadable_owner_says_so()
    {
        var refused = await DashboardActions.RanAsync(
            new Refusing(ExitCode.GeneralFailure), TimeProvider.System, new RunAction(Run, "close"), Quiet(), CancellationToken.None);

        refused.Error.Should().Contain("owner file could not be read");
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

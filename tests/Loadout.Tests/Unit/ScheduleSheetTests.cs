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
/// What the dashboard's schedule sheet tells somebody when an add is refused.
/// </summary>
/// <remarks>
/// A schedule called "SS Socials" was refused five times in a row for its
/// space, and every time the page said a schedule needs an interval, a time
/// of day or an event, at least five minutes apart, and not manual - all of
/// which the sheet had got right. Only the exit code crosses back from the
/// command, and that one is shared by every refusal it makes, so the page
/// guessed and guessed wrong.
/// </remarks>
public sealed class ScheduleSheetTests
{
    [Theory]
    [InlineData("SS Socials")]
    [InlineData("-socials")]
    [InlineData("socials/daily")]
    public async Task A_name_the_command_would_refuse_is_refused_for_its_name(string name)
    {
        var commands = new Refusing();

        var planned = await DashboardActions.PlannedAsync(
            commands,
            TimeProvider.System,
            new ScheduleAction("add", name, "marketing-studio", "check the socials", "starstats", At: "10:00"),
            Output(),
            CancellationToken.None);

        planned.Succeeded.Should().BeFalse();
        planned.Error.Should().Contain($"'{name}'").And.Contain("schedule name");
        planned.Error.Should().NotContain("five minutes", "the interval was never what was wrong");
        commands.Asked.Should().Be(0, "the answer is known before the command is typed");
    }

    [Fact]
    public async Task A_good_name_still_goes_to_the_command()
    {
        var commands = new Refusing();

        await DashboardActions.PlannedAsync(
            commands,
            TimeProvider.System,
            new ScheduleAction("add", "ss-socials", "marketing-studio", "check the socials", "starstats", At: "10:00"),
            Output(),
            CancellationToken.None);

        commands.Asked.Should().Be(1);
    }

    private static CommandOutput Output() =>
        new(
            AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(TextWriter.Null),
                Interactive = InteractionSupport.No,
            }),
            new GlobalSettings());

    /// <summary>A command that refuses whatever it is given, as the real one refuses a bad name.</summary>
    private sealed class Refusing : ICommandCatalogue
    {
        public int Asked { get; private set; }

        public IReadOnlyList<CatalogueEntry> Commands => [];

        public Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            Asked++;

            return Task.FromResult((int)ExitCode.InvalidArguments);
        }
    }
}

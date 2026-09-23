using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Tui;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Changing what this machine is set to, from the dashboard.
/// </summary>
/// <remarks>
/// The mapping, and the one rule that is not about mapping: where notices go is
/// a webhook address, and a webhook address <em>is</em> the credential - anybody
/// holding it can post into that channel as you. Every other control on this
/// page prints its whole command line where whoever started the server can see
/// it, which is right for them and wrong for exactly this one.
/// </remarks>
public sealed class SettingsPaneTests
{
    [Fact]
    public void Each_setting_types_the_command_that_owns_it()
    {
        Line(new SettingsChange("office", "open-office"))
            .Should().Be("config set team-office-set open-office");

        Line(new SettingsChange("waiting", "lobby"))
            .Should().Be("config set team-waiting-set lobby");

        Line(new SettingsChange("listen", "0.0.0.0"))
            .Should().Be("config set team-webhook-listen 0.0.0.0");

        Line(new SettingsChange("webhook-teams", "docs-crew"))
            .Should().Be("config set team-webhook-teams docs-crew");

        Line(new SettingsChange("webhook")).Should().Be("team webhook enable");
        Line(new SettingsChange("webhook", Off: true)).Should().Be("team webhook disable");

        Line(new SettingsChange("remedy", "restart-it"))
            .Should().Be("team remedy trust restart-it");

        Line(new SettingsChange("remedy", "restart-it", Off: true))
            .Should().Be("team remedy trust restart-it --revoke");
    }

    [Fact]
    public void Where_notices_go_carries_its_address_and_its_chat()
    {
        Line(new SettingsChange("notify", "telegram", Url: "https://example.invalid/x", Chat: "-100"))
            .Should().Be("team notify set telegram --url https://example.invalid/x --chat -100");

        // Clearing takes neither, and passing one would be an option the
        // command does not declare.
        Line(new SettingsChange("notify", Off: true)).Should().Be("team notify clear");
    }

    [Fact]
    public void A_setting_nothing_knows_about_is_refused_rather_than_guessed_at()
    {
        DashboardActions.Setting(new SettingsChange("purge-everything")).Command
            .Should().BeEmpty();
    }

    /// <remarks>
    /// The rule this file exists for. The address travels inwards, to the
    /// command that keeps it in the credential store, and no further: writing
    /// the line to the daemon's output would put it in a terminal, and from
    /// there into whatever is capturing that terminal.
    /// </remarks>
    [Fact]
    public async Task The_address_notices_go_to_is_never_written_to_the_output()
    {
        const string Secret = "https://hooks.example.invalid/T000/B000/xoxb-not-a-real-one";

        var written = new StringWriter();
        var output = new CommandOutput(Console(written), new GlobalSettings());

        await DashboardActions.SettledAsync(
            new Catalogue(),
            TimeProvider.System,
            new SettingsChange("notify", "slack", Url: Secret),
            output,
            CancellationToken.None);

        var said = written.ToString();

        said.Should().NotContain(Secret, "a webhook address is the credential, not a detail");
        said.Should().NotContain("xoxb-not-a-real-one");
        said.Should().NotContain("--url", "naming the option is most of the way to naming the value");

        // It still says something happened, because a page that can change this
        // machine should not change it silently.
        said.Should().Contain("notify");
    }

    [Fact]
    public async Task Turning_something_off_says_so_rather_than_saying_changed()
    {
        var written = new StringWriter();
        var output = new CommandOutput(Console(written), new GlobalSettings());

        await DashboardActions.SettledAsync(
            new Catalogue(),
            TimeProvider.System,
            new SettingsChange("webhook", Off: true),
            output,
            CancellationToken.None);

        written.ToString().Should().Contain("turned off");
    }

    /// <summary>The command line one change stands for, as one string.</summary>
    private static string Line(SettingsChange change)
    {
        var (command, arguments) = DashboardActions.Setting(change);

        return arguments.Count == 0 ? command : $"{command} {string.Join(' ', arguments)}";
    }

    private static IAnsiConsole Console(TextWriter into) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(into),
            Interactive = InteractionSupport.No,
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
        });

    /// <summary>A catalogue that runs nothing and says it worked.</summary>
    /// <remarks>
    /// What is under test here is what was written to the output on the way,
    /// not what the command did. Running the real one would set this machine's
    /// notification address from a unit test.
    /// </remarks>
    private sealed class Catalogue : ICommandCatalogue
    {
        public IReadOnlyList<CatalogueEntry> Commands => [];

        public Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default) =>
            Task.FromResult((int)ExitCode.Success);
    }
}

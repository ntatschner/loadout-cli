using FluentAssertions;
using Loadout.Cli;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What Studio's forms are told each command takes, read off the parser's own
/// settings types, and what the daemon refuses to run from the page.
/// </summary>
public sealed class StudioCommandsTests
{
    private static IReadOnlyList<StudioCommand> Catalogue()
    {
        Program.CommandNames().Should().NotBeEmpty("the catalogue is filled by registering the commands");

        return DashboardCommands.Build(Program.RegisteredCommands(), Loadout.Cli.Infrastructure.Catalogue.TypeOf);
    }

    private static StudioCommand Named(string path) => Catalogue().Single(one => one.Path == path);

    [Fact]
    public void A_command_s_arguments_and_options_are_the_ones_its_settings_declare()
    {
        var show = Named("memory show");

        show.Runnable.Should().BeTrue();
        show.Arguments.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "topic", Required = true, Many = false });

        show.Options.Should().Contain(option => option.Name == "--project" && option.Kind == "value" && !option.Global);
        show.Options.Should().Contain(option => option.Name == "--json" && option.Kind == "flag" && option.Global,
            "every command has --json, from the settings they all share");
        show.Options.Should().Contain(option => option.Name == "--quiet" && option.Short == "-q");
    }

    [Fact]
    public void Repeatable_and_optional_valued_options_are_told_apart_from_plain_ones()
    {
        Named("memory write").Options.Should().Contain(option => option.Name == "--fact" && option.Kind == "values");
        Named("memory show").Options.Should().Contain(option => option.Name == "--accessible" && option.Kind == "optional");
    }

    [Fact]
    public void A_branch_that_only_groups_others_is_listed_and_cannot_be_run()
    {
        var backup = Named("backup");

        backup.Runnable.Should().BeFalse();
        backup.Options.Should().BeEmpty();
    }

    [Fact]
    public void Each_command_carries_where_Studio_reaches_it()
    {
        Named("memory replace").Reach.Page.Should().Be("memory");
        Named("secret set").Reach.Effect.Should().Be(StudioEffect.TerminalOnly);
        Named("memory find").Reach.Effect.Should().Be(StudioEffect.Reads);
    }

    [Fact]
    public void A_terminal_only_command_is_refused_with_its_reason()
    {
        CommandJobs.Refusal(Named("secret set"), ["anthropic/default"], watchOnly: false)
            .Should().Contain("masked prompt").And.Contain("Run it in a terminal");
    }

    [Fact]
    public void A_command_that_starts_a_session_is_sent_to_Sessions()
    {
        CommandJobs.Refusal(Named("launch"), ["storefront"], watchOnly: false).Should().Contain("Sessions");
    }

    [Fact]
    public void A_branch_is_refused_rather_than_run_for_its_help()
    {
        CommandJobs.Refusal(Named("backup"), [], watchOnly: false).Should().Contain("group of commands");
    }

    [Fact]
    public void Watching_runs_what_reads_and_previews_of_what_changes_but_nothing_that_changes()
    {
        CommandJobs.Refusal(Named("memory find"), ["build"], watchOnly: true).Should().BeNull();
        CommandJobs.Refusal(Named("memory delete"), ["old-topic", "--dry-run"], watchOnly: true).Should().BeNull();
        CommandJobs.Refusal(Named("memory delete"), ["old-topic"], watchOnly: true).Should().Contain("only watching");
        CommandJobs.Refusal(Named("memory delete"), ["old-topic"], watchOnly: false).Should().BeNull();
    }

    [Fact]
    public void An_option_the_command_does_not_have_is_refused()
    {
        CommandJobs.Refusal(Named("memory show"), ["topic", "--yes"], watchOnly: false).Should().Contain("no option '--yes'");
    }

    [Fact]
    public void An_option_s_value_is_not_mistaken_for_an_argument_and_a_value_joined_with_equals_is_read_as_its_option()
    {
        CommandJobs.Refusal(Named("memory show"), ["topic", "--project", "starstats"], watchOnly: false).Should().BeNull();
        CommandJobs.Refusal(Named("memory show"), ["topic", "--project=-odd"], watchOnly: false).Should().BeNull();
        CommandJobs.Refusal(Named("memory show"), ["topic", "-q"], watchOnly: false).Should().BeNull("the short name is the same option");
    }

    [Fact]
    public void An_agreement_made_in_a_terminal_is_refused_on_a_command_whose_other_uses_are_fine()
    {
        var verify = Named("tools verify");

        CommandJobs.Refusal(verify, ["draft/1"], watchOnly: false).Should().BeNull();
        CommandJobs.Refusal(verify, ["draft/1", "--agree"], watchOnly: false).Should().Contain("--agree");
    }
}

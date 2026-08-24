using Loadout.Agents.Generic;
using Loadout.Cli;
using Loadout.Cli.Infrastructure;
using Loadout.Models;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The CLI surface is a product interface that scripts depend on
/// (spec sections 35 to 40), so the parts of it that must not drift are pinned
/// here rather than left to manual checking.
/// </summary>
public sealed class CliContractTests
{
    [Fact]
    public void Arguments_after_a_bare_separator_are_never_parsed()
    {
        // Spec section 36. The danger is concrete: --verbose is plausible on
        // both sides of the separator, and the launcher must not consume the
        // one meant for the agent.
        var (launcher, passthrough) = PassthroughArguments.Split(
            ["starstats", "--agent", "claude", "--", "--verbose", "--json"]);

        launcher.Should().Equal("starstats", "--agent", "claude");
        passthrough.Should().Equal("--verbose", "--json");
    }

    [Fact]
    public void Only_the_first_separator_splits_the_command_line()
    {
        // A second -- belongs to the agent and is passed through untouched.
        var (_, passthrough) = PassthroughArguments.Split(["p", "--", "a", "--", "b"]);

        passthrough.Should().Equal("a", "--", "b");
    }

    [Fact]
    public void A_command_line_without_a_separator_yields_no_passthrough()
    {
        var (launcher, passthrough) = PassthroughArguments.Split(["doctor", "--json"]);

        launcher.Should().Equal("doctor", "--json");
        passthrough.Should().BeEmpty();
    }

    [Fact]
    public void A_bare_project_name_becomes_a_launch()
    {
        // Spec section 35: loadout starstats is the shortest path and the one
        // people actually type.
        Program.Rewrite(["starstats"]).Should().Equal("launch", "starstats");
        Program.Rewrite(["starstats", "--agent", "codex"])
            .Should().Equal("launch", "starstats", "--agent", "codex");
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("status")]
    [InlineData("project")]
    [InlineData("workspace")]
    [InlineData("secret")]
    [InlineData("completion")]
    [InlineData("here")]
    [InlineData("launch")]
    public void Real_commands_are_not_mistaken_for_project_names(string command) =>
        Program.Rewrite([command]).Should().Equal(command);

    [Fact]
    public void Every_registered_command_reaches_its_command_rather_than_a_project()
    {
        // The list that guards this used to be written out by hand, and a
        // command nobody remembered to add to it did not fail loudly: it was
        // silently treated as a project name and reported that no project
        // matched it. Four commands added in one sitting all hit that.
        // Asserting over the registered set rather than a second hand-written
        // list is what keeps the guard honest.
        var names = Program.CommandNames();

        names.Should().NotBeEmpty();

        // Projected rather than asserted in a loop so a failure names the
        // offending command instead of reporting a length mismatch.
        var misrouted = names
            .Where(name => string.Join(' ', Program.Rewrite([name])) != name)
            .ToList();

        misrouted.Should().BeEmpty();
    }

    [Theory]
    [InlineData("drift")]
    [InlineData("sessions")]
    [InlineData("resume")]
    [InlineData("statusline")]
    public void Commands_added_later_are_registered_too(string command)
    {
        // Named explicitly as well, so deleting a registration is caught rather
        // than quietly shrinking the set the theory above iterates.
        Program.CommandNames().Should().Contain(command);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("--version")]
    [InlineData("--json")]
    public void Options_in_first_position_are_left_alone(string option) =>
        Program.Rewrite([option]).Should().Equal(option);

    [Fact]
    public void Exit_codes_keep_their_published_values()
    {
        // Spec section 40 makes these a public contract. Automation branches on
        // the number, so reordering the enum would silently break callers.
        ((int)ExitCode.Success).Should().Be(0);
        ((int)ExitCode.GeneralFailure).Should().Be(1);
        ((int)ExitCode.InvalidArguments).Should().Be(2);
        ((int)ExitCode.ProjectNotFound).Should().Be(3);
        ((int)ExitCode.RepositoryUnavailable).Should().Be(4);
        ((int)ExitCode.AgentUnavailable).Should().Be(5);
        ((int)ExitCode.WorkspaceSyncFailed).Should().Be(6);
        ((int)ExitCode.ConfigurationInvalid).Should().Be(7);
        ((int)ExitCode.AuthenticationRequired).Should().Be(8);
        ((int)ExitCode.PolicyViolation).Should().Be(9);
        ((int)ExitCode.GitConflict).Should().Be(10);
    }

    [Fact]
    public void The_executable_carries_a_real_version()
    {
        // loadout --version reads this. When the assembly has no version the
        // option still works but reports 0.0.0, which is worse than useless in
        // a bug report. Packaging stamps the real one in via -p:Version.
        var version = typeof(Program).Assembly.GetName().Version;

        version.Should().NotBeNull();
        version!.ToString(3).Should().NotBe("0.0.0");
    }

    [Fact]
    public void Generic_agent_placeholders_expand_from_the_launch_context()
    {
        var placeholders = new Dictionary<string, string>
        {
            ["REPOSITORY_PATH"] = "/home/test/git/starstats",
            ["PROJECT_SLUG"] = "starstats",
        };

        GenericAgentAdapter.Expand("--workspace=${REPOSITORY_PATH}", placeholders)
            .Should().Be("--workspace=/home/test/git/starstats");
    }

    [Fact]
    public void An_unknown_placeholder_is_left_visible_rather_than_blanked()
    {
        // Blanking it would produce a silently wrong argument; leaving it makes
        // the typo obvious in the failing command.
        GenericAgentAdapter.Expand("--flag=${NO_SUCH_THING}", new Dictionary<string, string>())
            .Should().Be("--flag=${NO_SUCH_THING}");
    }
}

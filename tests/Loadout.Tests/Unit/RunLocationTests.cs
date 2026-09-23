using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Tests.Platform;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether a run is about to happen somewhere other than where the person is.
/// </summary>
/// <remarks>
/// <para>
/// A team run works on the project's registered path, not on wherever the
/// command was typed. The second live run was started from one worktree and
/// worked on another, on a different branch, and nothing said so until the
/// journal was read afterwards.
/// </para>
/// <para>
/// The warning only earns its place by being rare. Said on every run - because
/// a trailing separator or a differently-cased drive letter counted as
/// different - it would be the line somebody skims past on the run that
/// mattered.
/// </para>
/// </remarks>
public sealed class RunLocationTests
{
    [Fact]
    public void Somewhere_else_is_somewhere_else()
    {
        TeamRunCommand.Elsewhere(
            Path.Combine(Path.GetTempPath(), "here"),
            Path.Combine(Path.GetTempPath(), "there"))
            .Should().BeTrue();
    }

    [Fact]
    public void The_same_place_is_not()
    {
        var one = Path.Combine(Path.GetTempPath(), "same");

        TeamRunCommand.Elsewhere(one, one).Should().BeFalse();
    }

    [Fact]
    public void A_trailing_separator_does_not_make_it_a_different_place()
    {
        // The failure this prevents is the warning appearing on every single
        // run, which is how a warning stops being read.
        var one = Path.Combine(Path.GetTempPath(), "same");

        TeamRunCommand.Elsewhere(one, one + Path.DirectorySeparatorChar).Should().BeFalse();
    }

    [Fact]
    public void A_relative_path_is_compared_as_the_place_it_means()
    {
        var here = Directory.GetCurrentDirectory();

        TeamRunCommand.Elsewhere(".", here).Should().BeFalse();
    }

    [WindowsFact]
    public void On_Windows_case_alone_is_the_same_directory()
    {
        // One directory there, two directories on a case-sensitive file
        // system, which is why this is asked of the platform rather than
        // assumed either way.
        var one = Path.Combine(Path.GetTempPath(), "Same");

        TeamRunCommand.Elsewhere(one, one.ToUpperInvariant()).Should().BeFalse();
    }
}

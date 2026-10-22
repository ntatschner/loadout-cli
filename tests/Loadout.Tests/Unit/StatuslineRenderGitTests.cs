using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Models.Configuration;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the status line asks git, through the helper its render command calls.
/// </summary>
/// <remarks>
/// The command itself reads the console's own input, which a test host cannot
/// safely hand it, so the git half is tested where it lives.
/// </remarks>
public sealed class StatuslineRenderGitTests
{
    [Fact]
    public async Task The_commit_time_is_not_asked_for_when_the_segment_is_off()
    {
        var git = new FakeGit("C:/repo") { HeadCommitTime = DateTimeOffset.UnixEpoch };
        var options = new StatuslineSettings();

        var (state, lastCommit) = await StatuslineRenderCommand.ReadGitAsync(
            git, "C:/repo", options, CancellationToken.None);

        state.Should().NotBeNull();
        lastCommit.Should().BeNull();
        git.HeadCommitTimeQueries.Should().Be(0, "the commit segment is off by default, and the query is a git process of its own");
    }

    [Fact]
    public async Task The_commit_time_is_asked_for_once_when_the_segment_is_on()
    {
        var git = new FakeGit("C:/repo") { HeadCommitTime = DateTimeOffset.UnixEpoch };
        var options = new StatuslineSettings { ShowLastCommit = true };

        var (_, lastCommit) = await StatuslineRenderCommand.ReadGitAsync(
            git, "C:/repo", options, CancellationToken.None);

        lastCommit.Should().Be(DateTimeOffset.UnixEpoch);
        git.HeadCommitTimeQueries.Should().Be(1);
    }

    [Fact]
    public void The_preview_copy_keeps_every_setting_and_turns_colour_off()
    {
        var options = new StatuslineSettings
        {
            ShowClock = true,
            ShowLastCommit = true,
            WaitingAfter = "5m",
            Time = new StatuslineTimeSettings { Absolute = true, Hour24 = false },
        };

        var copy = StatuslineShowCommand.Uncoloured(options);

        copy.Colour.Should().BeFalse();
        copy.ShowClock.Should().BeTrue();
        copy.ShowLastCommit.Should().BeTrue();
        copy.WaitingAfter.Should().Be("5m");
        copy.Time.Absolute.Should().BeTrue();
        copy.Time.Hour24.Should().BeFalse();
        copy.Time.Should().NotBeSameAs(options.Time);
        options.Colour.Should().BeTrue("the original is left as it was");
    }
}

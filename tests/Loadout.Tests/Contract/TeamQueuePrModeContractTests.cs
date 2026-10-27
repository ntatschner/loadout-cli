using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// 'team queue settings --pr-mode' from the built command line: setting a
/// queue's pull request mode, the old draft grant meaning the same as draft,
/// and handing the choice back to each project.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TeamQueuePrModeContractTests
{
    private const string Team = "iterating-project";

    [BuiltCliFact]
    public async Task A_queue_takes_a_mode_the_old_grant_means_draft_and_inherit_hands_it_back()
    {
        using var loadout = new LoadoutProcess();

        var ready = await loadout.RunAsync("team", "queue", "settings", Team, "--pr-mode", "ready", "--json");
        ready.ExitCode.Should().Be(0, ready.StandardError);
        var settings = ready.Json().GetProperty("settings");
        settings.GetProperty("prMode").GetString().Should().Be("ready");
        settings.GetProperty("draftPullRequests").GetBoolean().Should().BeFalse();

        var drafts = await loadout.RunAsync("team", "queue", "settings", Team, "--draft-prs", "on", "--json");
        drafts.ExitCode.Should().Be(0, drafts.StandardError);
        drafts.Json().GetProperty("settings").GetProperty("prMode").GetString().Should().Be("draft");
        drafts.Json().GetProperty("settings").GetProperty("draftPullRequests").GetBoolean().Should().BeTrue();

        var inherit = await loadout.RunAsync("team", "queue", "settings", Team, "--pr-mode", "inherit", "--json");
        inherit.ExitCode.Should().Be(0, inherit.StandardError);
        inherit.Json().GetProperty("settings").TryGetProperty("prMode", out _)
            .Should().BeFalse("JSON output leaves a null out, and inherit leaves the queue with no mode of its own");
        inherit.Json().GetProperty("settings").GetProperty("draftPullRequests").GetBoolean().Should().BeFalse();
    }

    [BuiltCliFact]
    public async Task A_word_that_is_not_a_mode_or_both_options_at_once_are_refused_before_anything_is_written()
    {
        using var loadout = new LoadoutProcess();

        var wrong = await loadout.RunAsync("team", "queue", "settings", Team, "--pr-mode", "yes");
        wrong.ExitCode.Should().Be(2);
        wrong.StandardError.Should().Contain("not a pull request mode");

        var both = await loadout.RunAsync("team", "queue", "settings", Team, "--pr-mode", "ready", "--draft-prs", "on");
        both.ExitCode.Should().Be(2);
        both.StandardError.Should().Contain("not both");

        var listed = await loadout.RunAsync("team", "queue", "list", Team, "--json");
        listed.ExitCode.Should().Be(0, listed.StandardError);
        listed.Json().GetProperty("settings").TryGetProperty("prMode", out _).Should().BeFalse("nothing was written");
    }
}

using FluentAssertions;
using Loadout.Core.Capabilities;
using Loadout.Core.Ideas;
using Loadout.Models.Capabilities;
using Loadout.Models.Ideas;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a refining agent is told a project lacks: the facts Loadout's checks
/// found, and that it may not claim more.
/// </summary>
public sealed class IdeaPromptGapsTests
{
    private static readonly IdeaRecord Record = new() { Id = "idea", Ask = "Ship it" };

    private static OfferSheet Sheet(IReadOnlyList<CapabilityOffer> offers, IReadOnlyList<CapabilityUnseen> unseen) =>
        new("site", "idea", DateTimeOffset.UnixEpoch, "not asked", offers, unseen, [], []);

    [Fact]
    public void Seen_gaps_and_what_could_not_be_looked_at_are_told_apart()
    {
        var prompt = IdeaWork.Prompt(Record, [], null, null, Sheet(
            [new CapabilityOffer("ci", "CI", CapabilityState.Gap, "No workflow runs.", true, [], [], null, false)],
            [new CapabilityUnseen("environments", CapabilityState.Unknown, "No GitHub token.")]));

        prompt.Should().Contain("## What the project lacks");
        prompt.Should().Contain("- ci: No workflow runs.");
        prompt.Should().Contain("- environments could not be looked at: No GitHub token.");
        prompt.Should().Contain("you may only suggest granting what it needs");
    }

    [Fact]
    public void With_nothing_to_say_the_section_is_left_out()
    {
        IdeaWork.Prompt(Record, [], null).Should().NotContain("What the project lacks");
        IdeaWork.Prompt(Record, [], null, null, Sheet([], [])).Should().NotContain("What the project lacks");
    }
}

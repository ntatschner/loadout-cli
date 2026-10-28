using System.Text.Json;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>How a team run's lines are coloured in a terminal.</summary>
/// <remarks>
/// Colour by meaning: what wants you, what went wrong, what moved on. And the
/// words must survive it untouched, since a pipe or NO_COLOR shows only those.
/// </remarks>
public sealed class TeamStyleTests
{
    private static RunEvent Event(string kind, string data = "{}", string? node = "implementer/1") =>
        new(DateTimeOffset.UtcNow, node, kind, JsonDocument.Parse(data).RootElement);

    [Theory]
    [InlineData("node.asked", "{}", "yellow")]
    [InlineData("node.answered", """{"allowed":true}""", "green")]
    [InlineData("node.answered", """{"allowed":false}""", "red")]
    [InlineData("node.failed", "{}", "red")]
    [InlineData("run.finished", "{}", "bold")]
    [InlineData("node.doing", """{"doing":"reading a.cs"}""", null)]
    public void A_line_is_coloured_by_what_it_means(string kind, string data, string? colour) =>
        TeamStyle.Colour(Event(kind, data)).Should().Be(colour);

    [Fact]
    public void A_run_silent_under_a_live_process_names_the_process_to_end_and_how_to_close_it()
    {
        var silent = new Liveness.Silent(
            TimeSpan.FromHours(2), new RunOwner(5150, new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero), "BOX", DateTimeOffset.UtcNow));

        var hint = Markup.Remove(TeamStyle.CloseHint("20261027-0900-ab12", silent));

        hint.Should().Contain("process 5150").And.Contain("halt waits").And.EndWith("loadout team runs close 20261027-0900-ab12");
        TeamStyle.LivenessWord(silent).Should().Be("silent");
        TeamStyle.Unowned(silent).Should().BeFalse("something still holds it, so it is never offered as one to end");
    }

    [Fact]
    public void Abandoned_finish_is_yellow() =>
        TeamStyle.Colour(Event("run.finished", """{"ended":"abandoned: closed by hand","outcome":"abandoned"}""", node: null))
            .Should().Be("yellow", "a run somebody ended for it did not finish on its own");

    [Fact]
    public void The_words_are_the_same_with_the_colour_taken_off()
    {
        var entry = Event("node.asked", """{"tool":"Bash","target":"rm [x]"}""");

        Markup.Remove(TeamStyle.Line(entry)).Should().Be(RunJournal.Describe(entry));
    }
}

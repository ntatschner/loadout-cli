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
    public void The_words_are_the_same_with_the_colour_taken_off()
    {
        var entry = Event("node.asked", """{"tool":"Bash","target":"rm [x]"}""");

        Markup.Remove(TeamStyle.Line(entry)).Should().Be(RunJournal.Describe(entry));
    }
}

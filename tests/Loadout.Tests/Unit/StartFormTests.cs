using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Teams;
using Spectre.Console.Cli;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The command line the dashboard's start form stands for.
/// </summary>
/// <remarks>
/// <para>
/// The page implements nothing: it types the command somebody would have typed.
/// That is only true while every box on the form reaches the command line, and
/// twice now one has not. The model was read from its box and dropped, so a run
/// started from the page took whatever the team file pinned however carefully
/// somebody had chosen otherwise — and nothing failed, because a field that is
/// read and discarded looks exactly like a field that works.
/// </para>
/// <para>
/// Asserted against the argument list rather than by running anything. Whether
/// the parser accepts these options is a different question, answered by the
/// contract tests against the built binary; this one is about whether they are
/// passed at all.
/// </para>
/// </remarks>
public sealed class StartFormTests
{
    private static StartRequest Filled() => new(
        "iterating-project",
        "Add --since to loadout usage.",
        Project: "demo",
        Rounds: 4,
        Autonomy: "supervised",
        Criteria: ["the option exists", "  ", "a test covers it"],
        Model: "opus",
        Agent: "claude",
        TakeRecommendationAfter: "30m");

    [Fact]
    public void Every_box_on_the_form_reaches_the_command_line()
    {
        var typed = DashboardActions.Starting(Filled());

        typed.Should().StartWith(["iterating-project", "Add --since to loadout usage."]);

        typed.Should().ContainInConsecutiveOrder("--project", "demo");
        typed.Should().ContainInConsecutiveOrder("--rounds", "4");
        typed.Should().ContainInConsecutiveOrder("--autonomy", "supervised");
        typed.Should().ContainInConsecutiveOrder("--model", "opus");
        typed.Should().ContainInConsecutiveOrder("--agent", "claude");
        typed.Should().ContainInConsecutiveOrder("--take-recommendation-after", "30m");

        // One option per criterion, because a criterion is a sentence and
        // sentences contain commas.
        typed.Should().Contain("--done-when=the option exists");
        typed.Should().Contain("--done-when=a test covers it");

        // Nobody is at the browser to answer a prompt in the daemon's console.
        typed.Should().Contain("--non-interactive");
    }

    [Fact]
    public void A_blank_criterion_is_dropped_rather_than_passed()
    {
        // A criterion nothing could report a verdict on would refuse every done
        // for ever, and a box people type into acquires empty lines.
        DashboardActions.Starting(Filled())
            .Count(one => one.StartsWith("--done-when=", StringComparison.Ordinal))
            .Should().Be(2);
    }

    [Fact]
    public void An_empty_form_asks_for_nothing_it_was_not_given()
    {
        var typed = DashboardActions.Starting(new StartRequest("docs-crew", "make the docs true"));

        typed.Should().NotContain("--project");
        typed.Should().NotContain("--model");
        typed.Should().NotContain("--agent");
        typed.Should().NotContain("--autonomy");
        typed.Should().NotContain(one => one.StartsWith("--done-when", StringComparison.Ordinal));
        typed.Should().NotContain("--take-recommendation-after");

        // And no round cap, which is what an empty box means now: the team's
        // budget and two rounds without progress are what stop it.
        typed.Should().NotContain("--rounds");
    }

    [Fact]
    public void A_round_cap_of_zero_is_no_cap_rather_than_a_cap_of_none()
    {
        // The form sends null for an empty box, but a zero reaching here from
        // anywhere else must mean the same thing rather than '--rounds 0',
        // which the runner would read as no cap anyway and the parser would
        // accept as a number.
        DashboardActions.Starting(new StartRequest("docs-crew", "goal", Rounds: 0))
            .Should().NotContain("--rounds");
    }
    /// <summary>
    /// "never" is what the box shows when it is empty, so it is what people type
    /// when they mean that. It was passed through, refused as not a duration,
    /// and reported on the page as a template that could not be run.
    /// </summary>
    [Theory]
    [InlineData("never")]
    [InlineData("Never")]
    [InlineData("off")]
    public void Never_asks_for_no_timed_answer(string typed)
    {
        DashboardActions.Starting(new StartRequest("docs-crew", "goal", TakeRecommendationAfter: typed))
            .Should().NotContain("--take-recommendation-after");
    }

    /// <summary>
    /// A criterion typed as a list item lost the whole run: "- tests pass"
    /// became an option with no name, and the parser refused everything.
    /// </summary>
    [Fact]
    public void A_criterion_typed_as_a_list_item_loses_its_marker()
    {
        var typed = DashboardActions.Starting(new StartRequest(
            "docs-crew",
            "- make the docs true",
            Criteria: ["- every page builds", "* nothing links nowhere", "2. the index is current"]));

        typed[1].Should().Be("make the docs true");
        typed.Should().Contain("--done-when=every page builds");
        typed.Should().Contain("--done-when=nothing links nowhere");
        typed.Should().Contain("--done-when=the index is current");
    }

    /// <summary>
    /// What the page types is only worth anything if the command reads it back
    /// as it was meant: this parses the page's own argument list with the
    /// command's own settings, and a criterion that still starts with a dash
    /// arrives as a criterion rather than as an option.
    /// </summary>
    [Fact]
    public void The_command_reads_back_what_the_page_typed()
    {
        var typed = DashboardActions.Starting(new StartRequest(
            "docs-crew",
            "make the docs true",
            Criteria: ["every page builds", "-40 degrees is still in range"],
            TakeRecommendationAfter: "30 mins"));

        var app = new CommandApp<Capture>();
        Capture.Last = null;

        app.Run([.. typed]).Should().Be(0);

        Capture.Last!.Goal.Should().Be("make the docs true");
        Capture.Last.DoneWhen.Should().Equal("every page builds", "-40 degrees is still in range");
        TeamDuration.Parse(Capture.Last.TakeRecommendationAfter).Should().Be(TimeSpan.FromMinutes(30));
    }

    [Theory]
    [InlineData("30m", 30)]
    [InlineData("30 mins", 30)]
    [InlineData("90 minutes", 90)]
    [InlineData("1 hour", 60)]
    [InlineData("1.5h", 90)]
    [InlineData("2 hrs", 120)]
    [InlineData("1d", 1440)]
    [InlineData("2 days", 2880)]
    public void A_duration_is_read_the_way_people_write_one(string text, int minutes)
    {
        TeamDuration.Parse(text).Should().Be(TimeSpan.FromMinutes(minutes));
    }

    [Theory]
    [InlineData("10")]
    [InlineData("never")]
    [InlineData("a fortnight")]
    [InlineData("0m")]
    [InlineData("")]
    public void Anything_else_is_not_a_duration(string text)
    {
        // A bare number is refused rather than guessed at: ten of one unit is
        // not ten of another.
        TeamDuration.Parse(text).Should().BeNull();
    }

    [Fact]
    public void A_refusal_says_what_would_be_accepted_and_nothing_about_templates()
    {
        var said = TeamDuration.Refusal(" 10 ");

        said.Should().Contain("'10'").And.Contain("30m").And.Contain("never");
        said.Should().NotContain("template");
    }

    /// <summary>Keeps what the parser made of the arguments, and runs nothing.</summary>
    private sealed class Capture : Command<TeamRunCommand.Settings>
    {
        public static TeamRunCommand.Settings? Last { get; set; }

        protected override int Execute(CommandContext context, TeamRunCommand.Settings settings, CancellationToken cancellationToken)
        {
            Last = settings;

            return 0;
        }
    }
}

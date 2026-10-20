using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// 'idea show' kept short by default, and every option's detail, pros and
/// cons under --full, as Studio shows them.
/// </summary>
public sealed class IdeaShowFullTests
{
    private static IdeaRecord Planned() => new()
    {
        Id = "status-page",
        Ask = "A status page for the home lab.",
        Plan = new IdeaPlan
        {
            Title = "Status page",
            Understanding = "A page showing what is up.",
            Layers =
            [
                new IdeaLayer
                {
                    Id = "L1",
                    Name = "Storage",
                    Chosen = "L1a",
                    Options =
                    [
                        new IdeaOption { Id = "L1a", Title = "SQLite", Detail = "One file beside the app.", Pros = ["Nothing to run"], Cons = ["One writer at a time"] },
                        new IdeaOption { Id = "L1b", Title = "Postgres", Detail = "A server of its own.", Pros = ["Many writers"], Cons = ["Another thing to keep up"] },
                    ],
                },
            ],
        },
    };

    private static string Shown(IdeaRecord record, bool full)
    {
        var written = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(written),
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
        });
        console.Profile.Width = 200;

        IdeaView.Write(new CommandOutput(console, new GlobalSettings()), new IdeaPlace(null, record.Id), record, full);

        return written.ToString();
    }

    [Fact]
    public void Without_full_only_the_chosen_option_s_detail_shows_and_the_flag_is_named()
    {
        var said = Shown(Planned(), full: false);

        said.Should().Contain("One file beside the app.");
        said.Should().NotContain("A server of its own.").And.NotContain("Many writers").And.NotContain("Nothing to run");
        said.Should().Contain("loadout idea show status-page --full");
    }

    [Fact]
    public void With_full_every_option_s_detail_pros_and_cons_show()
    {
        var said = Shown(Planned(), full: true);

        said.Should().Contain("One file beside the app.").And.Contain("A server of its own.");
        said.Should().Contain("+ Nothing to run").And.Contain("- One writer at a time");
        said.Should().Contain("+ Many writers").And.Contain("- Another thing to keep up");
        said.Should().NotContain("--full", "it is already full");
    }

    [Fact]
    public void A_plan_with_nothing_more_to_show_does_not_point_at_the_flag()
    {
        var bare = Planned();
        foreach (var option in bare.Plan!.Layers[0].Options)
        {
            option.Pros.Clear();
            option.Cons.Clear();
        }

        bare.Plan.Layers[0].Options[1].Detail = string.Empty;

        Shown(bare, full: false).Should().NotContain("--full");
    }
}

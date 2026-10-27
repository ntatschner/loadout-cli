using System.Text.Json;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Loadout.Tui.Terminal;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a question relates to, its context, its round's understanding and any
/// outdated marker, as 'idea show', the TUI and the JSON every other surface
/// reads each put it.
/// </summary>
public sealed class IdeaQuestionSurfacesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static IdeaReply.ReplyLayer Layer(string name, string purpose) => new()
    {
        Name = name,
        Purpose = purpose,
        Options = [new IdeaReply.ReplyOption { Title = "SQLite", Recommended = true }, new IdeaReply.ReplyOption { Title = "Postgres" }],
    };

    private static IdeaReply Planned(params IdeaReply.ReplyLayer[] layers) => new()
    {
        Contract = IdeaSchema.Version,
        Understanding = "A status page.",
        Plan = new IdeaReply.ReplyPlan { Title = "Status page", Layers = [.. layers] },
    };

    /// <summary>
    /// A plan, then a round that only asks, about L2, then a revision that
    /// reworks that layer, so the question is outdated.
    /// </summary>
    private static IdeaRecord Record(bool rework)
    {
        var record = new IdeaRecord { Id = "status-page", Ask = "A status page for the home lab" };

        IdeaWork.Merge(record, Planned(Layer("Interface", "Shows it."), Layer("Storage", "Keeps a week.")), Now);
        IdeaWork.Merge(record, new IdeaReply
        {
            Contract = IdeaSchema.Version,
            Understanding = "A page that keeps a week of checks.",
            Questions =
            [
                new IdeaReply.ReplyQuestion
                {
                    Question = "Keep a week of checks?",
                    Options = ["yes", "no"],
                    RelatesTo = "L2",
                    Context = "Storage sizes depend on it.",
                },
            ],
        }, Now);

        if (rework)
        {
            IdeaWork.Merge(record, Planned(Layer("Interface", "Shows it."), Layer("Storage", "Keeps a year.")), Now);
        }

        return record;
    }

    private static string Shown(IdeaRecord record)
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

        IdeaView.Write(new CommandOutput(console, new GlobalSettings()), new IdeaPlace(null, record.Id), record);

        return written.ToString();
    }

    [Fact]
    public void Idea_show_says_what_a_question_relates_to_under_it_and_the_round_s_understanding_above()
    {
        var said = Shown(Record(rework: false));

        said.Should().Contain("Understood as: A page that keeps a week of checks.");
        said.Should().Contain("Relates to L2 Storage: Storage sizes depend on it.");
        said.IndexOf("Understood as:", StringComparison.Ordinal)
            .Should().BeLessThan(said.IndexOf("Keep a week of checks?", StringComparison.Ordinal));
        said.Should().NotContain("possibly outdated");
    }

    [Fact]
    public void Idea_show_marks_a_question_whose_layer_was_reworked()
    {
        var said = Shown(Record(rework: true));

        said.Should().Contain("possibly outdated: layer Storage changed in revision 2");
        said.Should().Contain("1 possibly outdated");
    }

    [Fact]
    public void The_tui_detail_says_the_same()
    {
        var lines = string.Join('\n', IdeaSteps.Detail(new IdeaPlace(null, "status-page"), Record(rework: true), 200));

        lines.Should().Contain("Understood as: A page that keeps a week of checks.");
        lines.Should().Contain("Relates to L2 Storage: Storage sizes depend on it.");
        lines.Should().Contain("Possibly outdated: layer Storage changed in revision 2.");
        lines.Should().Contain("1 possibly outdated");
    }

    [Fact]
    public void The_json_carries_the_reference_context_snapshot_marker_and_understanding()
    {
        var record = Record(rework: true);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(IdeaJson.Of(new IdeaPlace(null, record.Id), record)));
        var root = json.RootElement;
        var question = root.GetProperty("questions")[0];

        question.GetProperty("relatesTo").GetString().Should().Be("L2");
        question.GetProperty("about").GetString().Should().Be("L2 Storage");
        question.GetProperty("context").GetString().Should().Be("Storage sizes depend on it.");
        question.GetProperty("snapshot").GetProperty("name").GetString().Should().Be("Storage");
        question.GetProperty("outdated").GetString().Should().Be("layer Storage changed in revision 2");
        root.GetProperty("rounds")[0].GetProperty("understanding").GetString().Should().Be("A page that keeps a week of checks.");
        root.GetProperty("outdated").GetInt32().Should().Be(1);
    }

    [Fact]
    public void A_record_from_before_idea_3_shows_and_serialises_with_no_reference_context_or_marker()
    {
        var record = new IdeaRecord
        {
            Id = "old",
            Ask = "Something",
            Rounds = [new IdeaRound { Questions = [new IdeaQuestion { Id = "Q1", Question = "Public?", Answer = "yes" }] }],
        };

        var said = Shown(record);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(IdeaJson.Of(new IdeaPlace(null, record.Id), record)));
        var question = json.RootElement.GetProperty("questions")[0];

        said.Should().Contain("Public?").And.NotContain("Relates to").And.NotContain("Understood as").And.NotContain("outdated");
        question.GetProperty("relatesTo").ValueKind.Should().Be(JsonValueKind.Null);
        question.GetProperty("outdated").ValueKind.Should().Be(JsonValueKind.Null);
    }
}

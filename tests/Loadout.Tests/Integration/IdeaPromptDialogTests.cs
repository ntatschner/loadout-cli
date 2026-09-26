using System.Drawing;
using FluentAssertions;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>The small dialogs the ideas screen asks its questions with.</summary>
public sealed class IdeaPromptDialogTests
{
    private const string LongQuestion =
        "Q3  Who needs to see the page, and from where: only you on the home network, or also from outside, "
        + "on a phone while away?\n\nWhy it matters: this decides whether you need a login and TLS.";

    private static IApplication App()
    {
        var app = Application.Create();
        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, 140, 40);

        return app;
    }

    [Fact]
    public void A_long_question_is_shown_in_full_above_its_choices()
    {
        using var app = App();
        using var dialog = new ExplainedChoiceDialog(LongQuestion, ["LAN only", "Tailscale too"], app);

        app.Begin(dialog);
        app.LayoutAndDraw();

        var shown = dialog.SubViews.OfType<Label>().Single(l => l.Id == "choice-question").Text;

        shown.Replace(Environment.NewLine, " ", StringComparison.Ordinal)
            .Should().Contain("on a phone while away?").And.Contain("a login and TLS",
                "the question is what is being answered, so none of it is cut off at the border");
    }

    [Fact]
    public void Enter_on_a_choice_answers_with_it()
    {
        using var app = App();
        using var dialog = new ExplainedChoiceDialog(LongQuestion, ["LAN only", "Tailscale too"], app);

        app.Begin(dialog);
        app.LayoutAndDraw();

        var list = dialog.SubViews.OfType<ListView>().Single(l => l.Id == "choice-list");
        list.SelectedItem = 1;
        list.NewKeyDownEvent(Key.Enter);

        dialog.ChosenIndex.Should().Be(1);
    }

    [Fact]
    public void Text_entry_keeps_what_was_typed_and_refuses_nothing_at_all()
    {
        using var app = App();
        using var dialog = new TextEntryDialog("Q1", ["Public or private?"], string.Empty, app);

        app.Begin(dialog);
        app.LayoutAndDraw();

        var field = dialog.SubViews.OfType<TextField>().Single(f => f.Id == "text-entry");
        field.NewKeyDownEvent(Key.Enter);
        dialog.Chosen.Should().BeNull("an empty answer is not an answer");

        field.Text = "  - only on the LAN  ";
        field.NewKeyDownEvent(Key.Enter);
        dialog.Chosen.Should().Be("- only on the LAN");
    }

    [Fact]
    public void Text_entry_starts_from_what_it_is_given_so_a_suggestion_can_be_accepted_as_it_is()
    {
        using var app = App();
        using var dialog = new TextEntryDialog("A new project", ["Called?"], "Lab watch", app);

        app.Begin(dialog);
        app.LayoutAndDraw();

        dialog.SubViews.OfType<TextField>().Single(f => f.Id == "text-entry").NewKeyDownEvent(Key.Enter);

        dialog.Chosen.Should().Be("Lab watch");
    }

    [Fact]
    public void A_marked_list_gives_what_was_ticked_and_nothing_ticked_is_no_answer()
    {
        using var app = App();
        using var dialog = new MarkedListDialog("Keep which?", "Space ticks.", ["L1 Storage", "L2 Interface", "A1 Alerts"], app);

        app.Begin(dialog);
        app.LayoutAndDraw();

        var ok = dialog.SubViews.OfType<Button>().Single(b => b.Text.Contains("OK", StringComparison.Ordinal));

        ok.InvokeCommand(Command.Accept);
        dialog.Chosen.Should().BeNull();

        var list = dialog.SubViews.OfType<ListView>().Single(l => l.Id == "marked-list");
        list.Source!.SetMark(0, true);
        list.Source.SetMark(2, true);

        ok.InvokeCommand(Command.Accept);
        dialog.Chosen.Should().Equal(0, 2);
    }
}

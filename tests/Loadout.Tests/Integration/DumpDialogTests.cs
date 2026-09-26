using System.Drawing;
using FluentAssertions;
using Loadout.Models.Ideas;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The launcher's two dump screens: the box notes are pasted into, and the list
/// of pieces to record. Each hands back an argument list for the command.
/// </summary>
public sealed class DumpDialogTests
{
    private static readonly DumpItem[] Pieces =
    [
        new() { Number = 1, Title = "Fix the flaky test", Kind = DumpItemKind.Task, Project = "website" },
        new() { Number = 2, Title = "Home lab status page", Kind = DumpItemKind.Idea },
        new() { Number = 4, Title = "Learn Rust", Kind = DumpItemKind.Idea },
    ];

    private static IApplication Started(Terminal.Gui.Views.Runnable build, IApplication app)
    {
        app.Begin(build);
        app.LayoutAndDraw();

        return app;
    }

    private static IApplication App()
    {
        var app = Application.Create();
        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, 140, 40);

        return app;
    }

    [Fact]
    public void Notes_with_spaces_and_quotes_reach_the_command_as_one_argument()
    {
        var notes = new DumpNotes("- fix the \"flaky\" test\n- a status page", OnProject: true, Split: true);

        notes.Arguments("website").Should().Equal("--text", "- fix the \"flaky\" test\n- a status page", "--project", "website");
        notes.Arguments(null).Should().Equal("--text", notes.Text, "--global");

        new DumpNotes("x", OnProject: false, Split: false).Arguments("website")
            .Should().Equal("--text", "x", "--global", "--no-split");
    }

    [Fact]
    public void Recording_every_piece_offered_names_none_and_fewer_names_them()
    {
        DumpPiecesDialog.Arguments("dump-1", null, [1, 2, 4], offered: 3)
            .Should().Equal("dump-1", "--global");

        DumpPiecesDialog.Arguments("dump-1", "website", [1, 4], offered: 3)
            .Should().Equal("dump-1", "--project", "website", "--only", "1,4");
    }

    [Fact]
    public void The_notes_box_draws_and_an_empty_box_is_not_an_answer()
    {
        using var app = App();
        using var dialog = new DumpNotesDialog("website", app);

        Started(dialog, app);

        var split = dialog.SubViews.OfType<Button>().Single(b => b.Text.Contains("Split", StringComparison.Ordinal));
        split.InvokeCommand(Command.Accept);

        dialog.Chosen.Should().BeNull("there is nothing in the box to keep");
    }

    [Fact]
    public void Pasted_notes_are_kept_on_the_project_unless_unticked()
    {
        using var app = App();
        using var dialog = new DumpNotesDialog("website", app);

        Started(dialog, app);

#pragma warning disable CS0618
        dialog.SubViews.OfType<TextView>().Single().Text = "- a status page";
#pragma warning restore CS0618

        dialog.SubViews.OfType<Button>().Single(b => b.Text.Contains("Keep", StringComparison.Ordinal))
            .InvokeCommand(Command.Accept);

        dialog.Chosen.Should().Be(new DumpNotes("- a status page", OnProject: true, Split: false));
    }

    [Fact]
    public void Every_piece_starts_ticked_and_what_is_unticked_is_left_out()
    {
        using var app = App();
        using var dialog = new DumpPiecesDialog("dump-1", Pieces, app);

        Started(dialog, app);

        var list = dialog.SubViews.OfType<ListView>().Single();
        Enumerable.Range(0, Pieces.Length).Should().OnlyContain(i => list.Source!.IsMarked(i));

        list.Source!.SetMark(1, false);

        dialog.SubViews.OfType<Button>().Single(b => b.Text.Contains("Record", StringComparison.Ordinal))
            .InvokeCommand(Command.Accept);

        dialog.Chosen.Should().Equal(1, 4);
    }

    [Fact]
    public void Nothing_ticked_is_not_an_answer()
    {
        using var app = App();
        using var dialog = new DumpPiecesDialog("dump-1", Pieces, app);

        Started(dialog, app);

        dialog.SubViews.OfType<ListView>().Single().MarkAll(false);

        dialog.SubViews.OfType<Button>().Single(b => b.Text.Contains("Record", StringComparison.Ordinal))
            .InvokeCommand(Command.Accept);

        dialog.Chosen.Should().BeNull();
    }
}

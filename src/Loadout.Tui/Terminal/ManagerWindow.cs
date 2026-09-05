using System.Collections.ObjectModel;
using Loadout.Core.Manager;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// One screen for what is loaded: packs, skills and MCP servers.
/// </summary>
/// <remarks>
/// <para>
/// The three answer "what is loaded and why" in three different places today —
/// a pack through its approval standing, a skill through where in the library
/// it was found, a server through the scope it was declared at — and somebody
/// wanting the whole answer had to ask three times and join it up themselves.
/// </para>
/// <para>
/// Every key here runs the command somebody would otherwise have typed. The
/// screen never approves a pack or writes a server itself: the trust boundary
/// is one implementation in <c>PackGate</c>, and a screen that had its own copy
/// would be a second one to keep in step.
/// </para>
/// <para>
/// Adding a server asks first, and is the only thing here that does. An MCP
/// server is not data — it is a program that runs with the agent's reach — so
/// it is the one action on this screen worth a question of its own.
/// </para>
/// </remarks>
internal sealed class ManagerWindow : Window
{
    private readonly ListView _rows;
    private readonly Label _detail;
    private readonly IApplication _application;
    private readonly string _slug;

    /// <summary>The row at each list position, or null where the line is a heading.</summary>
    private readonly List<ManagedItem?> _at = [];

    /// <summary>
    /// The command to run once this screen has closed, or null if nothing was
    /// asked for.
    /// </summary>
    internal string? Chosen { get; private set; }

    internal ManagerWindow(string slug, ManagerView view, IApplication application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;
        _slug = slug;

        Title = "Manager";
        BorderStyle = LineStyle.Rounded;

        var listFrame = new FrameView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(6),
            Title = "Loaded",
            BorderStyle = LineStyle.Single,
        };

        // Named so a test can find it by what it is rather than where it sits.
        // Selecting a view by position picks the wrong one the moment anything
        // above it changes height, and reads as a fresh bug.
        _rows = new ListView
        {
            Id = "manager-rows",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        _rows.SetSource(new ObservableCollection<string>(Lines(view)));
        _rows.ValueChanged += (_, _) => ShowDetail();

        listFrame.Add(_rows);

        var detailFrame = new FrameView
        {
            X = 0,
            Y = Pos.Bottom(listFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Title = "About",
            BorderStyle = LineStyle.Single,
        };

        _detail = new Label
        {
            Id = "manager-detail",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        detailFrame.Add(_detail);

        Add(listFrame, detailFrame);

        this.Bind(Key.Esc, Command.Quit);

        AddCommand(Command.Quit, () =>
        {
            _application.RequestStop();
            return true;
        });

        // A distinct Command per key, not three registrations of one. AddCommand
        // keys its handler on the Command, so binding all three to Accept
        // leaves the last one registered answering for every key — approve
        // would remove, which is the worst possible way to get this wrong.
        Bind(Key.A, Command.Save, () => Selected() is { Kind: ManagedKind.Pack } pack
            ? $"pack approve {pack.Name}"
            : null);

        Bind(Key.U, Command.Refresh, () => Selected() is { Kind: ManagedKind.Pack } pack
            ? $"pack update {pack.Name}"
            : null);

        // The one action here that asks before it acts. A pack is text an agent
        // reads and a skill is a procedure it follows; an MCP server is a
        // program that runs with the agent's reach, so importing one is not the
        // same kind of act as the rest of this screen and does not get the same
        // single keystroke.
        this.BindEverywhere(Key.N, Command.New);

        AddCommand(Command.New, () =>
        {
            if (Import() is { Length: > 0 } path)
            {
                Chosen = path;
                _application.RequestStop();
            }

            return true;
        });

        Bind(Key.R, Command.Cut, () => Selected() switch
        {
            { Kind: ManagedKind.Pack } pack => $"pack remove {pack.Name}",

            // Only what the workspace declared. An installed server belongs to
            // the agent's own configuration, and removing it from here would
            // report success having changed nothing this owns.
            { Kind: ManagedKind.Server, Scope: not "installed" } server =>
                $"mcp remove {server.Name} --project {_slug}"
                + (server.Scope == "every project" ? " --global" : string.Empty),

            _ => null,
        });

        ShowDetail();

        if (view.Notes.Count > 0)
        {
            // Said once, at the bottom, rather than repeated on every row it
            // applies to.
            _detail.Text = string.Join(Environment.NewLine, view.Notes);
        }
    }

    /// <summary>
    /// Binds a key to whatever command the selected row makes of it.
    /// </summary>
    /// <remarks>
    /// A key that does not apply to the selected row does nothing rather than
    /// reporting an error: pressing "approve" on a skill is a mis-aim, not a
    /// mistake worth a dialog.
    /// </remarks>
    private void Bind(Key key, Command slot, Func<string?> command)
    {
        this.BindEverywhere(key, slot);

        AddCommand(slot, () =>
        {
            if (command() is { Length: > 0 } path)
            {
                Chosen = path;
                _application.RequestStop();
            }

            return true;
        });
    }

    /// <summary>
    /// Asks what to import, then asks whether to.
    /// </summary>
    /// <remarks>
    /// Two questions rather than one. The first collects the name and what to
    /// run, which any argument would need; the second is the confirmation the
    /// decision asked for, and it says what is actually being agreed to — that
    /// this will be started alongside the agent and can do what the agent can.
    /// Returns null if either is dismissed, and nothing is run.
    /// </remarks>
    private string? Import()
    {
        var name = Ask("mcp add", "name", "github");

        if (name is not { Length: > 0 })
        {
            return null;
        }

        var target = Ask("mcp add " + name, "command or URL", "npx @modelcontextprotocol/server-github");

        if (target is not { Length: > 0 })
        {
            return null;
        }

        using var confirm = new ChoiceDialog(
            $"Add '{name}'? It runs alongside the agent and can do what the agent can.",
            ["No, leave it", $"Yes, add {name} to {_slug}"],
            _application);

        _application.Run(confirm);

        // Index 1 is the only yes. Dismissing the question, or the default
        // landing on the first row, both mean no — a confirmation that can be
        // passed by pressing Enter without reading is not a confirmation.
        return confirm.ChosenIndex == 1
            ? $"mcp add {name} {target} --project {_slug}"
            : null;
    }

    private string? Ask(string command, string argument, string example)
    {
        using var dialog = new CommandArgumentDialog(command, argument, example, _application);

        _application.Run(dialog);

        return dialog.Chosen;
    }

    private ManagedItem? Selected() =>
        _rows.SelectedItem is int index && index >= 0 && index < _at.Count
            ? _at[index]
            : null;

    private void ShowDetail()
    {
        if (Selected() is not { } item)
        {
            _detail.Text = "Esc closes this screen.";
            return;
        }

        var keys = item.Kind switch
        {
            ManagedKind.Pack => "a approve   u update   r remove",
            ManagedKind.Server when item.Scope != "installed" => "r remove",
            ManagedKind.Server => "installed by the agent, not managed here",
            ManagedKind.Plugin => "the agent's own: claude plugin enable/disable",
            _ => "skills are loaded when a task asks for them",
        };

        _detail.Text =
            $"{item.Name}{Environment.NewLine}"
            + $"from {item.Source}, at {item.Scope} scope — {item.Standing}"
            + $"{Environment.NewLine}{Environment.NewLine}{keys}";
    }

    /// <summary>
    /// The lines to draw, with a heading before each family.
    /// </summary>
    /// <remarks>
    /// A family with nothing in it still gets its heading and says so. Leaving
    /// it out would make "no packs declared" and "packs could not be read" look
    /// identical, which is the difference somebody opens this screen for.
    /// </remarks>
    private IReadOnlyList<string> Lines(ManagerView view)
    {
        var lines = new List<string>();

        void Section(string heading, ManagedKind kind)
        {
            lines.Add(heading);
            _at.Add(null);

            var of = view.OfKind(kind).ToList();

            if (of.Count == 0)
            {
                lines.Add("    none");
                _at.Add(null);
                return;
            }

            foreach (var item in of)
            {
                lines.Add(
                    $"  {(item.NeedsAttention ? "!" : " ")} {Trim(item.Name, 26)}"
                    + $"  {Trim(item.Source, 30)}  {Trim(item.Scope, 13)}  {item.Standing}");

                _at.Add(item);
            }
        }

        Section("SPECIALIST PACKS", ManagedKind.Pack);
        lines.Add(string.Empty);
        _at.Add(null);

        Section("MCP SERVERS", ManagedKind.Server);
        lines.Add(string.Empty);
        _at.Add(null);

        Section("SKILLS", ManagedKind.Skill);
        lines.Add(string.Empty);
        _at.Add(null);

        // Last, and read-only. These belong to the agent's own configuration:
        // Loadout can say what is installed and whether it is on, and changing
        // either is the agent's own command to run.
        Section("PLUGINS", ManagedKind.Plugin);

        return lines;
    }

    private static string Trim(string text, int width) =>
        text.Length <= width
            ? text.PadRight(width)
            : text[..(width - 1)] + "…";
}

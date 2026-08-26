using System.Collections.ObjectModel;
using Loadout.Core.Configuration;
using Loadout.Models.Configuration;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// What the settings screen was asked to change.
/// <para>
/// Values rather than a mutated config, so nothing is written by the act of
/// looking. The caller compares these against what was loaded and does only
/// what actually differs.
/// </para>
/// </summary>
/// <param name="Values">
/// Every setting the screen showed, keyed the way <c>loadout config</c> keys
/// it. Held as text because that is what was typed and what the registry's
/// own setters take; a value that will not parse is the caller's to report.
/// </param>
/// <param name="EditorProfiles">
/// Editor profile per agent. Empty values mean "no profile for that agent",
/// which is a real answer and not a missing one.
/// </param>
internal sealed record SettingsEdit(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, string> EditorProfiles);

/// <summary>
/// The launcher's settings, and where everything lives.
/// <para>
/// Every setting, generated from the same registry <c>loadout config</c> reads.
/// It used to name six of them by hand — workspace, branch, default agent, the
/// two sync policies and the editor command — out of twenty-one. The other
/// fifteen could only be reached by typing <c>loadout config set</c>, and
/// nothing said so: which terminal opens, where clones land, which directories
/// are scanned for repositories, where agents are looked for, the secrets
/// backend, the update feed, and every part of the agent status line were all
/// invisible to anybody who opened the screen meant for changing settings.
/// </para>
/// <para>
/// Generated rather than listed, so that gap cannot reopen. A setting added to
/// the registry appears here without anybody remembering to add it, and a test
/// asserts exactly that.
/// </para>
/// <para>
/// Nothing is saved from here. The screen hands back what was typed and closes,
/// and the caller writes it — which matters more than it sounds, because
/// changing the workspace repository has to move an existing clone aside first,
/// and that is not something to do while a screen is still drawing.
/// </para>
/// </summary>
internal sealed class SettingsWindow : Window
{
    private const int LabelWidth = 24;

    /// <summary>Read-only entries have no key, so they are named by this.</summary>
    /// <remarks>
    /// Short because it has to fit the column at eighty characters, which is
    /// the floor this application is meant to work at. "Where things are kept"
    /// read better and was cut to "Where things are ke" — the same defect the
    /// project list had, rebuilt from scratch in a new screen a day after it
    /// was fixed in the old one.
    /// </remarks>
    private const string Places = "Paths";

    private readonly IApplication _application;

    /// <summary>One field per setting, keyed the way the registry keys it.</summary>
    private readonly Dictionary<string, TextField> _fields = new(StringComparer.Ordinal);

    /// <summary>
    /// One tick per yes-or-no setting, keyed the way the registry keys it.
    /// </summary>
    private readonly Dictionary<string, CheckBox> _flags = new(StringComparer.Ordinal);

    /// <summary>One field per installed agent, keyed by the agent's name.</summary>
    private readonly Dictionary<string, TextField> _profiles = [];

    /// <summary>One page per group, shown when its group is chosen.</summary>
    private readonly Dictionary<string, View> _pages = new(StringComparer.Ordinal);

    private readonly ListView _groups;

    private readonly Label _hint;

    /// <summary>What was typed, or null if the screen was dismissed.</summary>
    internal SettingsEdit? Edit { get; private set; }

    /// <summary>
    /// Every setting this screen can change, keyed the way the registry keys
    /// it.
    /// </summary>
    /// <remarks>
    /// Exposed so a test can hold the screen against the registry rather than
    /// against a list somebody wrote down. The list somebody wrote down is
    /// what went wrong: six of twenty-one settings had fields and the gap was
    /// invisible from either side.
    /// </remarks>
    internal IReadOnlyCollection<string> Editable => [.. _fields.Keys, .. _flags.Keys];

    /// <param name="config">The settings as they stand.</param>
    /// <param name="machine">
    /// This machine's own settings. Separate because they must never travel to
    /// another machine, and two of the keys shown here live in it.
    /// </param>
    /// <param name="places">Where things are kept, as label and path pairs.</param>
    /// <param name="agents">Installed agents, so a profile can be mapped to each.</param>
    /// <param name="editorName">The editor's command, named in the hint.</param>
    /// <param name="known">
    /// Profiles the editor actually has, so a name can be checked by eye rather
    /// than mistyped and discovered later as an empty window.
    /// </param>
    /// <param name="application">The running application.</param>
    internal SettingsWindow(
        LauncherConfig config,
        MachineConfig machine,
        IReadOnlyList<(string Label, string Value)> places,
        IReadOnlyList<string> agents,
        string editorName,
        IReadOnlyList<string> known,
        IApplication application)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        Title = "Settings";
        BorderStyle = LineStyle.Rounded;

        // Grouped down the side rather than scrolled. Twenty-one settings do
        // not fit a short terminal, and a scrolling pane whose fields take the
        // focus one at a time is a screen where the thing you are typing into
        // can be off the top of it.
        var shown = SectionsOf(agents, places);

        Sections = shown;

        _groups = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ShowMarks = false,
        };

        _groups.SetSource(new ObservableCollection<string>(shown));

        // Wide enough for the longest section and no wider. Twenty-eight per
        // cent was a third of an eighty-column terminal and still too narrow
        // for the longest name, while wasting a dozen columns on a wide one.
        var columnWidth = shown.Max(section => section.Length) + 4;

        var groupFrame = new FrameView
        {
            X = 0,
            Y = 0,
            Width = Dim.Absolute(columnWidth),
            Height = Dim.Fill(3),
            Title = "Sections",
            BorderStyle = LineStyle.Rounded,
        };

        groupFrame.Add(_groups);

        var pageFrame = new FrameView
        {
            X = Pos.Right(groupFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(3),
            Title = shown[0],
            BorderStyle = LineStyle.Rounded,
        };

        foreach (var group in shown)
        {
            var page = BuildPage(group, config, machine, agents, editorName, known, places);

            page.Visible = false;

            _pages[group] = page;

            pageFrame.Add(page);
        }

        // Said as the field is reached rather than crammed beside it. The
        // descriptions are sentences — "Seconds a launch-time fetch may block
        // before going offline" — and there is no column wide enough for
        // twenty-one of those next to the field they describe.
        _hint = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(1),
            Text = string.Empty,
        };

        var save = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_Save", IsDefault = true };
        var cancel = new Button { X = Pos.Right(save) + 2, Y = Pos.AnchorEnd(1), Text = "_Close" };

        _groups.ValueChanged += (_, _) =>
        {
            if (_groups.SelectedItem is not int index || index < 0 || index >= shown.Count)
            {
                return;
            }

            Show(shown[index]);

            pageFrame.Title = shown[index];
        };

        save.Accepting += (_, e) =>
        {
            e.Handled = true;

            var values = _fields.ToDictionary(
                pair => pair.Key,
                pair => (pair.Value.Text ?? string.Empty).Trim(),
                StringComparer.Ordinal);

            foreach (var (key, box) in _flags)
            {
                // In the spelling the registry's own setter takes back, so a
                // tick and a typed "true" are the same edit.
                values[key] = box.Value == CheckState.Checked ? "true" : "false";
            }

            Edit = new SettingsEdit(
                values,
                _profiles.ToDictionary(
                    pair => pair.Key,
                    pair => (pair.Value.Text ?? string.Empty).Trim(),
                    StringComparer.Ordinal));

            _application.RequestStop(this);
        };

        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(groupFrame, pageFrame, _hint, save, cancel);

        Show(shown[0]);

        _groups.SelectedItem = 0;
    }

    /// <summary>
    /// The groups worth showing, in the registry's own order, plus the paths.
    /// </summary>
    private static List<string> SectionsOf(
        IReadOnlyList<string> agents,
        IReadOnlyList<(string Label, string Value)> places)
    {
        var groups = ConfigKeys.Groups.InOrder
            .Where(group => ConfigKeys.All.Any(entry => Belongs(entry, group, agents)))
            .ToList();

        if (places.Count > 0)
        {
            groups.Add(Places);
        }

        return groups;
    }

    /// <summary>
    /// Whether a setting appears under a group when the screen is built.
    /// </summary>
    /// <remarks>
    /// editor-profiles is the one setting not shown as itself. Its value is a
    /// map written "claude=Agents;codex=Codex", and a single field holding a
    /// syntax somebody has to look up is worse than a row per installed agent —
    /// so the Editor group draws those rows instead, and this hides the raw
    /// key so it is not asked for twice.
    /// </remarks>
    private static bool Belongs(ConfigKeys.Entry entry, string group, IReadOnlyList<string> agents) =>
        string.Equals(entry.Group, group, StringComparison.Ordinal)
        && (entry.Key != "editor-profiles" || agents.Count > 0);

    private View BuildPage(
        string group,
        LauncherConfig config,
        MachineConfig machine,
        IReadOnlyList<string> agents,
        string editorName,
        IReadOnlyList<string> known,
        IReadOnlyList<(string Label, string Value)> places)
    {
        var page = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

        if (string.Equals(group, Places, StringComparison.Ordinal))
        {
            for (var i = 0; i < places.Count; i++)
            {
                page.Add(new Label { X = 1, Y = i, Text = places[i].Label });
                page.Add(new Label { X = 1 + LabelWidth, Y = i, Text = places[i].Value });
            }

            return page;
        }

        var row = 0;

        foreach (var entry in ConfigKeys.All.Where(e => Belongs(e, group, agents)))
        {
            if (entry.Key == "editor-profiles")
            {
                row = AddProfiles(page, config, agents, editorName, known, row);
                continue;
            }

            var current = entry.Read(config, machine) ?? string.Empty;

            View control;

            if (entry.IsFlag)
            {
                var box = new CheckBox
                {
                    X = 1 + LabelWidth,
                    Y = row,
                    Text = string.Empty,
                    Value = IsYes(current) ? CheckState.Checked : CheckState.UnChecked,
                };

                page.Add(new Label { X = 1, Y = row, Text = entry.Key });
                page.Add(box);

                _flags[entry.Key] = box;

                control = box;
            }
            else
            {
                var field = Field(page, entry.Key, current, row);

                _fields[entry.Key] = field;

                control = field;
            }

            control.HasFocusChanged += (_, e) =>
            {
                if (e.NewValue)
                {
                    _hint.Text = entry.Description;
                }
            };

            row++;
        }

        return page;
    }

    /// <summary>
    /// A row per installed agent, rather than one field holding a syntax
    /// somebody has to look up.
    /// </summary>
    private int AddProfiles(
        View page,
        LauncherConfig config,
        IReadOnlyList<string> agents,
        string editorName,
        IReadOnlyList<string> known,
        int row)
    {
        page.Add(new Label
        {
            X = 1,
            Y = row + 1,
            Width = Dim.Fill(1),
            Text = "Editor profile per agent",
        });

        row += 2;

        foreach (var agent in agents)
        {
            var field = Field(page, agent, config.Editor.Profiles.GetValueOrDefault(agent, string.Empty), row);

            field.HasFocusChanged += (_, e) =>
            {
                if (e.NewValue)
                {
                    _hint.Text = $"Which {editorName} profile opens {agent}. Blank means no profile.";
                }
            };

            _profiles[agent] = field;

            row++;
        }

        page.Add(new Label
        {
            X = 1,
            Y = row,
            Width = Dim.Fill(1),
            Text = known.Count == 0
                ? "The editor reported no profiles, so a name typed here will be created."
                : $"Profiles {editorName} has: {string.Join(", ", known)}",
        });

        return row + 1;
    }

    /// <summary>The sections, in the order the list offers them.</summary>
    internal IReadOnlyList<string> Sections { get; }

    /// <summary>Opens a section, as choosing it in the list does.</summary>
    internal void Open(int section)
    {
        _groups.SelectedItem = section;
    }

    /// <summary>Shows one group and hides the rest.</summary>
    private void Show(string group)
    {
        foreach (var (name, page) in _pages)
        {
            page.Visible = string.Equals(name, group, StringComparison.Ordinal);
        }

        _hint.Text = string.Empty;
    }

    /// <summary>
    /// Reads a flag the way the registry's own setter reads one, so a value
    /// somebody typed as "yes" does not come back unticked and get written
    /// out as "false".
    /// </summary>
    private static bool IsYes(string value) =>
        value.Trim().ToLowerInvariant() is "true" or "yes" or "on" or "1";

    private static TextField Field(View parent, string label, string value, int row)
    {
        parent.Add(new Label { X = 1, Y = row, Text = label });

        var field = new TextField
        {
            X = 1 + LabelWidth,
            Y = row,
            Width = Dim.Fill(2),
            Text = value,
        };

        parent.Add(field);

        return field;
    }
}

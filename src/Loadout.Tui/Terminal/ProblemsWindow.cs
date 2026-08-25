using System.Collections.ObjectModel;
using Loadout.Models.Diagnostics;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// One remedy, with what it says it would do.
/// </summary>
/// <param name="Remedy">The fix itself.</param>
/// <param name="Preview">
/// What it reported it would change, worked out before this screen opened.
/// Previewing is a read, but it is a read of a repository, and doing it while
/// the screen was drawing would stall it.
/// </param>
internal sealed record OfferedRemedy(Remedy Remedy, string Preview);

/// <summary>
/// What is wrong with a project, and what can be done about it.
/// <para>
/// Nothing is applied from here. The screen collects what somebody ticked and
/// closes, and the fixes are carried out with the terminal handed back — the
/// same rule the launcher follows for launching an agent, and for the same
/// reason: applying a fix writes to a repository and can take long enough that
/// a screen doing it while still drawing would look like it had hung.
/// </para>
/// </summary>
internal sealed class ProblemsWindow : Window
{
    private readonly IReadOnlyList<OfferedRemedy> _offered;
    private readonly ListView _remedies;
    private readonly Label _preview;
    private readonly IApplication _application;

    /// <summary>Which remedies were ticked. Empty unless Apply was chosen.</summary>
    internal IReadOnlyList<Remedy> Chosen { get; private set; } = [];

    internal ProblemsWindow(
        string project,
        IReadOnlyList<DiagnosticCheck> findings,
        IReadOnlyList<OfferedRemedy> offered,
        IApplication application)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(application);

        _offered = offered;
        _application = application;

        Title = $"Problems — {project}";
        BorderStyle = LineStyle.Rounded;

        // Info-level findings are not problems, and listing them here would
        // bury the ones that are.
        var worth = findings
            .Where(finding => finding.Severity != DiagnosticSeverity.Info)
            .ToList();

        var findingsFrame = new FrameView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(45),
            Title = worth.Count == 0 ? "Nothing wrong" : "Found",
            BorderStyle = LineStyle.Single,
        };

        var findingsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

        findingsList.SetSource(new ObservableCollection<string>(
            worth.Count == 0
                ? ["Nothing here needs attention."]
                : worth.Select(f =>
                    $"{(f.Severity == DiagnosticSeverity.Error ? "✖" : "!")} {f.Name} — {f.Detail}")));

        findingsFrame.Add(findingsList);

        var remediesFrame = new FrameView
        {
            X = 0,
            Y = Pos.Bottom(findingsFrame),
            Width = Dim.Percent(50),
            Height = Dim.Fill(2),
            Title = "Can be put right",
            BorderStyle = LineStyle.Single,
        };

        // Ticked rather than applied on selection, so nothing is changed by
        // moving around the list.
        _remedies = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ShowMarks = true,
            MarkMultiple = true,
        };

        _remedies.SetSource(new ObservableCollection<string>(
            offered.Count == 0
                ? ["None of these can be put right automatically."]
                : offered.Select(o => o.Remedy.Description)));

        remediesFrame.Add(_remedies);

        var previewFrame = new FrameView
        {
            X = Pos.Right(remediesFrame),
            Y = Pos.Bottom(findingsFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            Title = "What it would change",
            BorderStyle = LineStyle.Single,
        };

        _preview = new Label { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

        previewFrame.Add(_preview);

        var apply = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_Apply ticked" };
        var close = new Button { X = Pos.Right(apply) + 2, Y = Pos.AnchorEnd(1), Text = "_Close" };

        apply.Enabled = offered.Count > 0;

        apply.Accepting += (_, e) =>
        {
            e.Handled = true;

            Chosen = [.. Enumerable.Range(0, _offered.Count)
                .Where(i => _remedies.Source?.IsMarked(i) == true)
                .Select(i => _offered[i].Remedy)];

            _application.RequestStop(this);
        };

        close.Accepting += (_, e) =>
        {
            e.Handled = true;
            _application.RequestStop(this);
        };

        _remedies.ValueChanged += (_, _) => ShowPreview();

        KeyBindings.Add(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(findingsFrame, remediesFrame, previewFrame, apply, close);

        ShowPreview();
    }

    private void ShowPreview()
    {
        _preview.Text = _remedies.SelectedItem is int index
            && index >= 0
            && index < _offered.Count
                ? _offered[index].Preview
                : string.Empty;
    }
}

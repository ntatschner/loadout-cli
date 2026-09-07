using System.Collections.ObjectModel;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>What a launch was asked to do, beyond which project.</summary>
/// <param name="Task">
/// What the session is for, in the words somebody would use. This is what
/// chooses specialists, so it is the field that changes what the agent knows.
/// </param>
/// <param name="Mode">How to work: advise, implement, investigate or review.</param>
/// <param name="Offline">Do not reach the network.</param>
/// <param name="NoSync">Do not synchronise the workspace first.</param>
/// <param name="Agent">Which agent to start, or null for the project's default.</param>
/// <param name="Profile">Which context profile, or null for the project's own settings.</param>
/// <param name="Worktree">
/// Which working tree, by branch or directory name, or null for the main one.
/// </param>
/// <param name="IncludeHandoff">Append the most recent handoff to the context.</param>
internal sealed record LaunchOptions(
    string? Task = null,
    string? Mode = null,
    bool Offline = false,
    bool NoSync = false,
    string? Agent = null,
    string? Profile = null,
    string? Worktree = null,
    bool IncludeHandoff = false);

/// <summary>One thing that can be picked, and what picking it means.</summary>
/// <param name="Label">What is shown.</param>
/// <param name="Value">
/// What reaches the launch, or null for "the default", which a launch spells
/// as the absence of the option.
/// </param>
internal sealed record LaunchChoice(string Label, string? Value);

/// <summary>
/// What a project offers a launch to choose between, beyond agents and modes.
/// </summary>
/// <param name="Profiles">Context profiles the chosen agent can use, the default first.</param>
/// <param name="Worktrees">Working trees of the repository, the main one first.</param>
internal sealed record LaunchChoices(
    IReadOnlyList<LaunchChoice> Profiles,
    IReadOnlyList<LaunchChoice> Worktrees)
{
    /// <summary>A project with nothing to choose: one profile, one working tree.</summary>
    internal static readonly LaunchChoices None = new(
        [new LaunchChoice("default", null)],
        [new LaunchChoice("main working tree", null)]);
}

/// <summary>What the preview is asked about: the launch as it stands.</summary>
internal sealed record LaunchPreviewRequest(
    ProjectResolution Project,
    string Agent,
    string? Profile,
    string? Task,
    string? Mode);

/// <summary>
/// Where the sheet gets what it shows.
/// </summary>
/// <param name="Choices">
/// The profiles and working trees a project offers, given the agent.
/// </param>
/// <param name="Preview">
/// What a launch as described would load. The same resolver the launch itself
/// reads, so what the sheet shows is what the session gets.
/// </param>
internal sealed record LaunchSheetSources(
    Func<ProjectResolution, string, CancellationToken, Task<LaunchChoices>> Choices,
    Func<LaunchPreviewRequest, CancellationToken, Task<EffectiveInstructions?>> Preview);

/// <summary>
/// The launch sheet: everything a launch can be asked, on one screen, with
/// what the answers would load shown beneath them.
/// </summary>
/// <remarks>
/// <para>
/// This used to be a dialog behind a menu item, and Enter on a project
/// launched with none of it. A launch request carries fifteen things and the
/// screen filled three; the agent could not be chosen at all, and the task and
/// the mode — the two that decide which specialists the session is given —
/// sat behind an unadvertised menu entry. The command line could do all of it,
/// which made the screen a strict subset of it.
/// </para>
/// <para>
/// Now it is the launch. Every path that starts a session comes through here,
/// filled in with the defaults, so Enter on the project and Enter again is the
/// fast path and costs one keystroke more than before. What that keystroke
/// buys is the preview: the specialists the task would select and what they
/// cost, re-resolved as the task is typed, before anything is spent on them.
/// </para>
/// <para>
/// Nothing here selects a specialist. The preview asks the same resolver the
/// launch reads and shows the answer, so the sheet cannot disagree with the
/// session it starts.
/// </para>
/// </remarks>
internal sealed class LaunchOptionsDialog : Window
{
    /// <summary>
    /// The modes a task can be worked in, and no mode at all.
    /// </summary>
    /// <remarks>
    /// Named here and defined in the specialist library, so a test holds the
    /// two together. A mode offered on a screen that no specialist answers to
    /// would be a choice that silently does nothing, which is the shape of
    /// several faults this launcher has already had.
    /// </remarks>
    internal static readonly string[] Modes =
        ["(let the task decide)", "advise", "implement", "investigate", "review"];

    /// <summary>
    /// How long after the last keystroke the preview is re-resolved.
    /// </summary>
    /// <remarks>
    /// Resolving reads the repository, which is too much to do on every
    /// character of a sentence. Long enough that a word gets typed in one go;
    /// short enough that the pause after it reads as the answer arriving.
    /// </remarks>
    internal static readonly TimeSpan DefaultPreviewDelay = TimeSpan.FromMilliseconds(350);

    private const string Waiting = "working out what this session would load…";

    private readonly ProjectResolution _project;
    private readonly IReadOnlyList<string> _agents;
    private readonly LaunchSheetSources? _sources;
    private readonly TimeSpan _previewDelay;
    private readonly IApplication _application;

    private readonly TextField _task;
    private readonly ListView _agent;
    private readonly ListView _mode;
    private readonly ListView _profile;
    private readonly ListView _worktree;
    private readonly CheckBox _offline;
    private readonly CheckBox _noSync;
    private readonly CheckBox _handoff;
    private readonly ListView _preview;

    private LaunchChoices _choices = LaunchChoices.None;

    /// <summary>Cancels a preview or a choice read that is no longer wanted.</summary>
    private CancellationTokenSource? _pending;

    private CancellationTokenSource? _readingChoices;

    /// <summary>The timer waiting for typing to stop, when one is running.</summary>
    private object? _debounce;

    /// <summary>What was chosen, or null when the sheet was dismissed.</summary>
    internal LaunchOptions? Chosen { get; private set; }

    /// <summary>
    /// Builds the sheet for one project.
    /// </summary>
    /// <param name="project">The project to launch.</param>
    /// <param name="agents">Agents installed on this machine, in the order to offer them.</param>
    /// <param name="application">The application the sheet runs on.</param>
    /// <param name="sources">
    /// Where profiles, working trees and the preview come from, or null for a
    /// sheet that offers only what it already knows.
    /// </param>
    /// <param name="previewDelay">
    /// How long to wait after typing before re-resolving; the default unless a
    /// test needs it to be nothing.
    /// </param>
    internal LaunchOptionsDialog(
        ProjectResolution project,
        IReadOnlyList<string> agents,
        IApplication application,
        LaunchSheetSources? sources = null,
        TimeSpan? previewDelay = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(application);

        _project = project;
        _application = application;
        _sources = sources;
        _previewDelay = previewDelay ?? DefaultPreviewDelay;

        // The project's own agent first, whatever order the machine reports
        // them in, and offered even when it is not detected here: a launch
        // that cannot find it says so in words, which is more use than a
        // sheet that quietly offers something else.
        var preferred = project.Entry.DefaultAgent;

        _agents = [
            preferred,
            .. agents.Where(a => !string.Equals(a, preferred, StringComparison.OrdinalIgnoreCase)),
        ];

        Title = $"Launch {project.Entry.Name}";
        Width = Dim.Fill(2);
        Height = Dim.Fill(1);
        BorderStyle = LineStyle.Rounded;

        Add(new Label { X = 1, Y = 0, Text = "What are you about to do?" });

        _task = new TextField { X = 1, Y = 1, Width = Dim.Fill(1) };

        Add(new Label
        {
            X = 1,
            Y = 2,
            Text = "This chooses the specialists the agent is given.",
        });

        // Four pickers side by side, each a short list, so the whole sheet
        // fits an 80x24 terminal with the preview still visible beneath.
        const int pickerTop = 4;
        const int pickerHeight = 5;

        _agent = Picker("Agent", 1, 15, pickerTop, pickerHeight, _agents);
        _mode = Picker("Mode", 17, 23, pickerTop, pickerHeight, Modes);
        _profile = Picker("Profile", 41, 18, pickerTop, pickerHeight, Labels(_choices.Profiles));
        _worktree = Picker("Worktree", 60, 0, pickerTop, pickerHeight, Labels(_choices.Worktrees));

        _offline = new CheckBox { X = 1, Y = pickerTop + pickerHeight + 1, Text = "Work _offline" };
        _noSync = new CheckBox { X = 20, Y = pickerTop + pickerHeight + 1, Text = "Skip workspace _sync" };
        _handoff = new CheckBox { X = 44, Y = pickerTop + pickerHeight + 1, Text = "Append last _handoff" };

        var previewFrame = new FrameView
        {
            X = 0,
            Y = pickerTop + pickerHeight + 3,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            Title = "This session would load",
        };

        // A list rather than a text view, for the same reason the rest of the
        // launcher uses one: it is read, never edited, and each line stands on
        // its own.
        _preview = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = false,
        };

        ShowLines(sources is null ? [] : [Waiting]);

        previewFrame.Add(_preview);

        var launch = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_Launch", IsDefault = true };
        var cancel = new Button { X = Pos.Right(launch) + 2, Y = Pos.AnchorEnd(1), Text = "Cance_l" };

        launch.Accepting += (_, e) => { e.Handled = true; Accept(); };
        cancel.Accepting += (_, e) => { e.Handled = true; Dismiss(); };

        // Enter in the text field starts the launch rather than doing nothing,
        // because somebody who has just typed what they are about to do has
        // finished answering the only question that needed them.
        _task.Accepting += (_, e) => { e.Handled = true; Accept(); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { Dismiss(); return true; });

        Add(_task, _offline, _noSync, _handoff, previewFrame, launch, cancel);

        _task.TextChanged += (_, _) => PreviewSoon();
        _agent.ValueChanged += (_, _) => { ReadChoices(); PreviewNow(); };
        _mode.ValueChanged += (_, _) => PreviewNow();
        _profile.ValueChanged += (_, _) => PreviewNow();

        _task.SetFocus();

        ReadChoices();
        PreviewNow();
    }

    /// <summary>The agent the sheet would launch, as it stands.</summary>
    private string CurrentAgent =>
        _agent.SelectedItem is int index && index >= 0 && index < _agents.Count
            ? _agents[index]
            : _project.Entry.DefaultAgent;

    private string? CurrentMode =>
        // Index zero is "let the task decide", which means no mode rather than
        // a mode called that.
        _mode.SelectedItem is int index && index > 0 && index < Modes.Length
            ? Modes[index]
            : null;

    private string? CurrentTask
    {
        get
        {
            var typed = _task.Text?.Trim();

            return string.IsNullOrWhiteSpace(typed) ? null : typed;
        }
    }

    private string? CurrentProfile => ValueOf(_profile, _choices.Profiles);

    private string? CurrentWorktree => ValueOf(_worktree, _choices.Worktrees);

    private static string? ValueOf(ListView list, IReadOnlyList<LaunchChoice> choices) =>
        list.SelectedItem is int index && index >= 0 && index < choices.Count
            ? choices[index].Value
            : null;

    private static IReadOnlyList<string> Labels(IReadOnlyList<LaunchChoice> choices) =>
        choices.Select(c => c.Label).ToList();

    private ListView Picker(
        string name,
        int x,
        int width,
        int top,
        int height,
        IReadOnlyList<string> items)
    {
        Add(new Label { X = x, Y = top, Text = name });

        var list = new ListView
        {
            X = x,
            Y = top + 1,
            Width = width > 0 ? width : Dim.Fill(1),
            Height = height,
        };

        list.SetSource(new ObservableCollection<string>(items));
        list.SelectedItem = 0;

        Add(list);

        return list;
    }

    /// <summary>
    /// Asks what the project offers for the agent now chosen, and fills the
    /// profile and working-tree pickers when the answer arrives.
    /// </summary>
    private void ReadChoices()
    {
        if (_sources is null)
        {
            return;
        }

        _readingChoices?.Cancel();
        _readingChoices?.Dispose();
        _readingChoices = new CancellationTokenSource();

        var token = _readingChoices.Token;
        var reading = _sources.Choices(_project, CurrentAgent, token);

        if (reading.IsCompletedSuccessfully)
        {
            Offer(reading.Result);
            return;
        }

        _ = System.Threading.Tasks.Task.Run(
            async () =>
            {
                LaunchChoices choices;

                try
                {
                    choices = await reading.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    // A project whose profiles or working trees cannot be read
                    // can still be launched with the defaults, which is what
                    // the pickers already say.
                    return;
                }

                if (!token.IsCancellationRequested)
                {
                    _application.Invoke(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            Offer(choices);
                        }
                    });
                }
            },
            CancellationToken.None);
    }

    private void Offer(LaunchChoices choices)
    {
        _choices = choices;

        _profile.SetSource(new ObservableCollection<string>(Labels(choices.Profiles)));
        _profile.SelectedItem = 0;

        _worktree.SetSource(new ObservableCollection<string>(Labels(choices.Worktrees)));
        _worktree.SelectedItem = 0;
    }

    /// <summary>Re-resolves once typing has paused.</summary>
    private void PreviewSoon()
    {
        if (_sources is null)
        {
            return;
        }

        if (_debounce is not null)
        {
            _application.RemoveTimeout(_debounce);
            _debounce = null;
        }

        if (_previewDelay <= TimeSpan.Zero)
        {
            PreviewNow();
            return;
        }

        _debounce = _application.AddTimeout(_previewDelay, () =>
        {
            _debounce = null;
            PreviewNow();

            return false;
        });
    }

    /// <summary>
    /// Asks what the launch as it stands would load, and shows the answer.
    /// </summary>
    private void PreviewNow()
    {
        if (_sources is null)
        {
            return;
        }

        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();

        var token = _pending.Token;

        var request = new LaunchPreviewRequest(
            _project, CurrentAgent, CurrentProfile, CurrentTask, CurrentMode);

        var resolving = _sources.Preview(request, token);

        // Already known — a source with nothing to wait for. Shown now rather
        // than after a trip through the main loop, so a test can read it and a
        // person does not see "working out…" flash for one frame.
        if (resolving.IsCompletedSuccessfully)
        {
            Show(resolving.Result, failure: null);
            return;
        }

        ShowLines([Waiting]);

        _ = System.Threading.Tasks.Task.Run(
            async () =>
            {
                EffectiveInstructions? effective = null;
                string? failure = null;

                try
                {
                    effective = await resolving.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Shown rather than swallowed: a preview that says nothing
                    // is indistinguishable from one that has not arrived yet.
                    failure = ex.Message;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                _application.Invoke(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        Show(effective, failure);
                    }
                });
            },
            CancellationToken.None);
    }

    private void Show(EffectiveInstructions? effective, string? failure)
    {
        ShowLines(failure is { Length: > 0 }
            ? [$"could not work out what this session would load: {failure}"]
            : effective is null
                ? ["nothing could be worked out for this project"]
                : Describe(effective));
    }

    private void ShowLines(IReadOnlyList<string> lines) =>
        _preview.SetSource(new ObservableCollection<string>(lines));

    /// <summary>
    /// The preview, one line per specialist, grouped as the explain command
    /// groups them, with what it costs at the end.
    /// </summary>
    /// <remarks>
    /// Foundation is collapsed to one line. It loads whatever the task, so
    /// four lines saying so on every launch would be the first thing anybody
    /// learned to skip, and the line under them is the one that changes.
    /// </remarks>
    internal static IReadOnlyList<string> Describe(EffectiveInstructions effective)
    {
        ArgumentNullException.ThrowIfNull(effective);

        var lines = new List<string>();

        var foundation = effective.OfKind(SpecialistKind.Foundation).ToList();

        if (foundation.Count > 0)
        {
            lines.Add(
                $"{"foundation",-12} {string.Join(", ", foundation.Select(s => s.Specialist.Title))}");
        }

        foreach (var selection in effective.Selected)
        {
            if (selection.Specialist.Kind == SpecialistKind.Foundation)
            {
                continue;
            }

            var kind = selection.Specialist.Kind.ToString().ToLowerInvariant();

            lines.Add($"{kind,-12} {selection.Specialist.Title,-24} {selection.Reason}");
        }

        foreach (var omitted in effective.DroppedForBudget)
        {
            lines.Add($"{"not loaded",-12} {omitted.Specialist.Id,-24} {omitted.Reason}");
        }

        var budget = effective.Budget;

        lines.Add(string.Empty);
        lines.Add(budget.TokenBudget > 0
            ? $"{"specialists",-12} about {budget.EstimatedTokens:N0} tokens, "
                + $"{budget.UsedFraction * 100:N0}% of {budget.TokenBudget:N0}"
                + (budget.IsOverBudget ? " — over budget" : string.Empty)
            : $"{"specialists",-12} about {budget.EstimatedTokens:N0} tokens");

        if (effective.EvidenceTruncated)
        {
            lines.Add("the repository was too large to scan in full; what was detected may be incomplete");
        }

        return lines;
    }

    private void Accept()
    {
        Chosen = new LaunchOptions(
            CurrentTask,
            CurrentMode,
            _offline.Value == CheckState.Checked,
            _noSync.Value == CheckState.Checked,
            CurrentAgent,
            CurrentProfile,
            CurrentWorktree,
            _handoff.Value == CheckState.Checked);

        Stop();
    }

    private void Dismiss()
    {
        Chosen = null;
        Stop();
    }

    private void Stop()
    {
        _pending?.Cancel();
        _readingChoices?.Cancel();

        if (_debounce is not null)
        {
            _application.RemoveTimeout(_debounce);
            _debounce = null;
        }

        _application.RequestStop(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _readingChoices?.Cancel();
            _readingChoices?.Dispose();
        }

        base.Dispose(disposing);
    }
}

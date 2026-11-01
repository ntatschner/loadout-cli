using System.Collections.ObjectModel;
using Loadout.Core.About;
using Loadout.Core.Updates;
using Loadout.Platform.Abstractions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// About Loadout: the installed version with Update now, the feature matrix,
/// the guides and the release timeline.
/// </summary>
/// <remarks>
/// <para>
/// Everything shown comes from <see cref="AboutModel"/>, the one model
/// <c>loadout about --json</c> and Studio's page read too, so no surface can
/// name a guide or a release the others do not. In particular no URL is
/// written here: a guide is opened at the address the model gives.
/// </para>
/// <para>
/// The update is not run in this process. It is <c>loadout update --yes
/// --progress</c> started as a piped child by the runner handed in, and this
/// window only shows the lines it reports, so the screen and the command line
/// cannot update in two different ways.
/// </para>
/// </remarks>
internal sealed class AboutWindow : Window
{
    private readonly IApplication _application;
    private readonly IApplicationLauncher _opener;
    private readonly Func<CancellationToken, IAsyncEnumerable<UpdateProgress>> _update;

    private readonly Label _version;
    private readonly Button _updateNow;
    private readonly Button _quit;
    private readonly Label _progress;
    private readonly TableView _features;
    private readonly ListView _guides;
    private readonly ListView _timeline;
    private readonly Label _timelineEmpty;
    private readonly List<string> _lines = [];

    private AboutModel _model;
    private bool _updating;
    private UpdateStage? _lastStage;

    internal AboutWindow(
        AboutModel model,
        Func<CancellationToken, IAsyncEnumerable<UpdateProgress>> update,
        IApplicationLauncher opener,
        IApplication application)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(opener);
        ArgumentNullException.ThrowIfNull(application);

        _model = model;
        _update = update;
        _opener = opener;
        _application = application;

        Title = "About Loadout";
        BorderStyle = LauncherTheme.Lines;

        // Every section and every view a test reads is named, so a test finds
        // it by what it is rather than where it sits.
        var versionFrame = new FrameView
        {
            Id = "about-version-section",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 8,
            Title = "Version",
            BorderStyle = LauncherTheme.Inner,
        };

        _version = new Label { Id = "about-version", X = 0, Y = 0, Width = Dim.Fill(16) };

        _updateNow = new Button { Id = "about-update", X = Pos.AnchorEnd(), Y = 0, Text = "_Update now" };
        _updateNow.Accepting += (_, e) =>
        {
            e.Handled = true;

            // Never cancelled: stopping the child mid-install could leave no working launcher.
            _ = UpdateAsync(CancellationToken.None);
        };

        _quit = new Button { Id = "about-quit", X = Pos.AnchorEnd(), Y = 0, Text = "_Quit", Visible = false };
        _quit.Accepting += (_, e) =>
        {
            e.Handled = true;
            QuitChosen = true;
            _application.RequestStop(this);
        };

        _progress = new Label { Id = "about-progress", X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill() };

        versionFrame.Add(_version, _updateNow, _quit, _progress);

        var featuresFrame = new FrameView
        {
            Id = "about-features-section",
            X = 0,
            Y = Pos.Bottom(versionFrame),
            Width = Dim.Percent(60),
            Height = Dim.Percent(50),
            Title = "Features",
            BorderStyle = LauncherTheme.Inner,
        };

        _features = new TableView
        {
            Id = "about-features",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
        };

        featuresFrame.Add(_features);

        var guidesFrame = new FrameView
        {
            Id = "about-guides-section",
            X = Pos.Right(featuresFrame),
            Y = Pos.Bottom(versionFrame),
            Width = Dim.Fill(),
            Height = Dim.Percent(50),
            Title = "Guides",
            BorderStyle = LauncherTheme.Inner,
        };

        _guides = new ListView { Id = "about-guides", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

        // Accepting, not Accepted: the list never raises Accepted.
        _guides.Accepting += (_, e) =>
        {
            e.Handled = true;
            OpenGuide();
        };

        guidesFrame.Add(_guides);

        var timelineFrame = new FrameView
        {
            Id = "about-timeline-section",
            X = 0,
            Y = Pos.Bottom(featuresFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Title = "Releases",
            BorderStyle = LauncherTheme.Inner,
        };

        _timeline = new ListView { Id = "about-timeline", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _timeline.Accepting += (_, e) =>
        {
            e.Handled = true;
            ShowNotes();
        };

        _timelineEmpty = new Label
        {
            Id = "about-timeline-empty",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
        };

        timelineFrame.Add(_timeline, _timelineEmpty);

        Add(versionFrame, featuresFrame, guidesFrame, timelineFrame);

        this.Bind(Key.Esc, Command.Quit);

        // Closing mid-update is refused rather than cancelled: stopping the
        // child part-way through putting the new version in place could leave
        // no working launcher, and closing without stopping it would leave it
        // installing where nobody can see.
        AddCommand(Command.Quit, () =>
        {
            if (_updating)
            {
                // Commands arrive on the main loop, so this is shown directly.
                ShowStep(new UpdateProgress(UpdateStage.Checking, null, null, StillUpdatingText));
                return true;
            }

            _application.RequestStop(this);
            return true;
        });

        Show(model);
    }

    /// <summary>Whether the window was closed from the Quit offered after an update.</summary>
    internal bool QuitChosen { get; private set; }

    /// <summary>Whether an update finished and replaced this launcher's binary.</summary>
    internal bool Updated { get; private set; }

    /// <summary>The release lines as shown, newest first, for a test to read.</summary>
    internal IReadOnlyList<string> TimelineLines { get; private set; } = [];

    /// <summary>
    /// Shows a model, the kept one first and the refreshed one when it lands.
    /// Must be called on the main loop.
    /// </summary>
    internal void Show(AboutModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _model = model;

        if (!Updated)
        {
            _version.Text = VersionLine(model);
        }

        _features.Table = new EnumerableTableSource<AboutFeature>(
            model.Features,
            new Dictionary<string, Func<AboutFeature, object>>
            {
                ["Feature"] = feature => feature.Name,
                ["Since"] = feature => feature.Since,
                ["Status"] = feature => feature.Status,
                ["Installed"] = feature => feature.Installed ? "yes" : "not yet",
            });

        _guides.SetSource(new ObservableCollection<string>(model.Guides.Select(guide => guide.Title)));

        TimelineLines = model.Timeline.Releases.Select(ReleaseLine).ToList();
        _timeline.SetSource(new ObservableCollection<string>(TimelineLines));

        // A reason is said even when a kept list is there to show beside it,
        // because the list then is older than it looks.
        if (model.Timeline.Empty is { } empty && model.Timeline.Releases.Count == 0)
        {
            _timelineEmpty.Text = empty.Describe();
            _timelineEmpty.Visible = true;
            _timeline.Visible = false;
        }
        else
        {
            _timelineEmpty.Text = string.Empty;
            _timelineEmpty.Visible = false;
            _timeline.Visible = true;
        }

        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>
    /// Runs the update and shows each step it reports, as it reports it.
    /// </summary>
    /// <remarks>
    /// The steps cross to the main loop through the application's invoke,
    /// because the runner reads a child process's output on whatever thread
    /// that arrives.
    /// </remarks>
    internal async Task UpdateAsync(CancellationToken ct)
    {
        if (_updating || Updated)
        {
            return;
        }

        _updating = true;
        _lines.Clear();

        var installing = false;
        var handedToMainLoop = false;
        UpdateProgress? last = null;

        try
        {
            await foreach (var step in _update(ct).WithCancellation(ct).ConfigureAwait(false))
            {
                installing |= step.Stage == UpdateStage.Installing;
                last = step;
                OnMainLoop(() => ShowStep(step));
            }

            if (installing && last is { Stage: UpdateStage.Done } done)
            {
                // Updated is set on the main loop, so the update is only over
                // once that has happened: cleared here instead, a close in
                // between would leave with Updated still false.
                handedToMainLoop = true;
                OnMainLoop(() =>
                {
                    ShowUpdated(UpdatedVersion(done, _model));
                    _updating = false;
                });
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            OnMainLoop(() => ShowStep(new UpdateProgress(UpdateStage.Done, null, null, "The update was stopped.")));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            var reason = ex.Message;
            OnMainLoop(() => ShowStep(new UpdateProgress(UpdateStage.Done, null, null, $"The update failed: {reason}")));
        }
        finally
        {
            if (!handedToMainLoop)
            {
                _updating = false;
            }
        }
    }

    /// <summary>The progress lines shown so far, for a test to read.</summary>
    internal string ProgressText => _progress.Text;

    private void ShowStep(UpdateProgress step)
    {
        var line = step is { Bytes: { } bytes, Total: { } total and > 0 }
            ? $"{step.Message} ({bytes * 100 / total}%)"
            : step.Message;

        // A download reports many times; one line for it, kept current.
        if (_lines.Count > 0 && step.Stage == UpdateStage.Downloading && _lastStage == UpdateStage.Downloading)
        {
            _lines[^1] = line;
        }
        else
        {
            _lines.Add(line);
        }

        _lastStage = step.Stage;
        _progress.Text = string.Join(Environment.NewLine, _lines);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>
    /// What the window says and offers once the binary has been replaced.
    /// </summary>
    /// <remarks>
    /// Kept apart because it is the answer to an open question, and is meant
    /// to be easy to change. Only Quit is offered: this process is still
    /// running from the bundle that was replaced, and anything it loads from
    /// now on can fail to load.
    /// </remarks>
    private void ShowUpdated(string version)
    {
        Updated = true;
        _version.Text = UpdatedText(version);
        _updateNow.Visible = false;
        _quit.Visible = true;
        _quit.SetFocus();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    internal const string StillUpdatingText =
        "The update is still running. Close this window once it has finished.";

    internal static string UpdatedText(string version) =>
        $"Updated to {version}. Restart Loadout to use it.";

    private static string UpdatedVersion(UpdateProgress done, AboutModel model)
    {
        const string Prefix = "Updated to ";

        return done.Message.StartsWith(Prefix, StringComparison.Ordinal)
            ? done.Message[Prefix.Length..].Trim()
            : model.Available ?? "the new version";
    }

    private void OnMainLoop(Action action)
    {
        try
        {
            _application.Invoke(action);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The window closed while the update was still reporting. Nobody
            // is left to show it to.
        }
    }

    private void OpenGuide()
    {
        if (_guides.SelectedItem is not { } index || index < 0 || index >= _model.Guides.Count)
        {
            return;
        }

        var url = _model.Guides[index].Url;

        // Only a web page. The table is shipped in the binary, and this is the
        // check that a bad row cannot turn into opening something local.
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = _opener.OpenUrlAsync(url);
    }

    private void ShowNotes()
    {
        if (_timeline.SelectedItem is not { } index || index < 0 || index >= _model.Timeline.Releases.Count)
        {
            return;
        }

        var release = _model.Timeline.Releases[index];

        // Plain text, never rendered: the notes are whatever was published.
        // TextView is marked obsolete in favour of an editor in a separate
        // package; a read-only box of wrapped text is not worth a dependency.
#pragma warning disable CS0618
        var notes = new TextView
        {
            Id = "about-notes",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            ReadOnly = true,
            WordWrap = true,
            Text = release.Notes.Length > 0 ? release.Notes : "No notes were published with this release.",
        };
#pragma warning restore CS0618

        var close = new Button { Text = "_Close", IsDefault = true };

        using var dialog = new Dialog
        {
            Title = string.IsNullOrWhiteSpace(release.Name) ? release.Version : release.Name,
            Width = Dim.Percent(80),
            Height = Dim.Percent(80),
        };

        close.Accepting += (_, e) =>
        {
            e.Handled = true;
            _application.RequestStop(dialog);
        };

        dialog.Add(notes);
        dialog.AddButton(close);

        _application.Run(dialog);
    }

    private static string VersionLine(AboutModel model)
    {
        var line = $"Loadout {model.Version}" + (model.DevBuild ? " (development build)" : string.Empty);

        return model.UpdateAvailable && model.Available is { Length: > 0 } available
            ? $"{line} · {available} available"
            : line;
    }

    internal static string ReleaseLine(AboutRelease release)
    {
        var published = release.Published is { } when ? when.ToString("yyyy-MM-dd") : "          ";
        var line = $"{release.Version,-10} {published}  {release.Kind,-7}";

        return release.Installed ? line : $"{line}  not installed";
    }
}

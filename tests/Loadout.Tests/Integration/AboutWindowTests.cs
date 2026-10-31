using System.Runtime.CompilerServices;
using Loadout.Core.About;
using Loadout.Core.Projects;
using Loadout.Core.Updates;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The launcher's About window: reached from Tools, showing the four sections
/// of the one About model, and following an update it did not run itself.
/// </summary>
public sealed class AboutWindowTests
{
    private static AboutModel Model(
        IReadOnlyList<AboutRelease>? releases = null,
        TimelineEmpty? empty = null,
        IReadOnlyList<AboutGuide>? guides = null) =>
        new(
            "0.109.5",
            false,
            "0.110.0",
            true,
            [new AboutFeature("teams", "Teams", "0.40.0", "stable", "teams", true)],
            guides ?? [new AboutGuide("teams", "Teams guide", "https://github.com/ntatschner/loadout-cli/blob/main/docs/teams.md")],
            new AboutTimeline(releases ?? [], empty, null, false));

    private static AboutRelease Release(string version, bool installed) =>
        new(version, $"Release {version}", DateTimeOffset.Parse("2026-10-30T00:00:00Z"), "notes", "https://example.test", installed, "feature");

    private static async IAsyncEnumerable<UpdateProgress> NoUpdate([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    private static TuiSession About(
        AboutModel model,
        out AboutWindow window,
        Func<CancellationToken, IAsyncEnumerable<UpdateProgress>>? update = null,
        IApplicationLauncher? opener = null)
    {
        AboutWindow? built = null;

        var session = TuiSession.Start(app => built = new AboutWindow(model, update ?? NoUpdate, opener ?? new RecordingOpener(), app));

        window = built!;

        return session;
    }

    private static T Find<T>(View root, string id)
        where T : View =>
        Descendants(root).OfType<T>().Single(view => view.Id == id);

    private static IEnumerable<View> Descendants(View root) =>
        root.SubViews.SelectMany(view => (IEnumerable<View>)[view, .. Descendants(view)]);

    [Fact]
    public void Tools_menu_has_about_entry()
    {
        LauncherWindow? built = null;

        using var session = TuiSession.Start(app => built = new LauncherWindow(
            [new ProjectResolution(new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha", DefaultAgent = "claude" }, "/repos/x", null, 0, false)],
            null,
            "workspace connected",
            ["claude"],
            (_, _) => Task.FromResult<ProjectOverview?>(null),
            _ => { },
            [],
            app));

        var window = built!;

        // Found by its title, as somebody reading the menu would find it.
        var item = Descendants(window)
            .OfType<MenuBarItem>()
            .Single(bar => bar.Title == "_Tools")
            .PopoverMenu!.Root!
            .GetMenuItemsOfAllSubMenus()
            .Single(entry => entry.Title == "_About Loadout…");

        item.Action!.Invoke();

        window.Intent!.Action.Should().Be(LauncherAction.About);
    }

    [Fact]
    public void Shows_four_sections()
    {
        using var session = About(Model(releases: [Release("0.109.5", installed: true)]), out var window);

        string[] sections = ["about-version-section", "about-features-section", "about-guides-section", "about-timeline-section"];

        foreach (var id in sections)
        {
            Find<FrameView>(window, id).Visible.Should().BeTrue(id);
        }

        var screen = session.Screen;

        screen.Should().Contain("0.109.5");
        screen.Should().Contain("Teams guide");
        screen.Should().Contain("stable");
    }

    [Fact]
    public async Task Update_now_shows_each_progress_line()
    {
        static async IAsyncEnumerable<UpdateProgress> Three([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new UpdateProgress(UpdateStage.Checking, null, null, "Checking the release source");
            yield return new UpdateProgress(UpdateStage.Verifying, null, null, "Verifying the download");
            yield return new UpdateProgress(UpdateStage.Extracting, null, null, "Unpacking the archive");
        }

        using var session = About(Model(), out var window, Three);

        await window.UpdateAsync(CancellationToken.None);
        session.Pump();

        var progress = Find<Label>(window, "about-progress").Text;

        progress.Should().Contain("Checking the release source");
        progress.Should().Contain("Verifying the download");
        progress.Should().Contain("Unpacking the archive");

        // Nothing was installed, so nothing says it was.
        window.Updated.Should().BeFalse();
    }

    [Fact]
    public async Task A_finished_update_says_restart_and_offers_only_quit()
    {
        static async IAsyncEnumerable<UpdateProgress> Installed([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new UpdateProgress(UpdateStage.Installing, null, null, "Putting the new version in place");
            yield return new UpdateProgress(UpdateStage.Done, null, null, "Updated to 0.110.0");
        }

        using var session = About(Model(), out var window, Installed);

        await window.UpdateAsync(CancellationToken.None);
        session.Pump();

        Find<Label>(window, "about-version").Text.Should().Be("Updated to 0.110.0. Restart Loadout to use it.");
        Find<Button>(window, "about-update").Visible.Should().BeFalse();
        Find<Button>(window, "about-quit").Visible.Should().BeTrue();
    }

    [Fact]
    public async Task Quit_after_an_update_closes_the_window()
    {
        static async IAsyncEnumerable<UpdateProgress> Installed([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new UpdateProgress(UpdateStage.Installing, null, null, "Putting the new version in place");
            yield return new UpdateProgress(UpdateStage.Done, null, null, "Updated to 0.110.0");
        }

        using var session = About(Model(), out var window, Installed);

        await window.UpdateAsync(CancellationToken.None);
        session.Pump();

        // Pump stops the loop after one turn and leaves the stop flag set,
        // which would pass this test whatever Quit did.
        window.StopRequested = false;

        Find<Button>(window, "about-quit").InvokeCommand(Command.Accept);

        // The old binary is gone, so staying open is not an option.
        window.QuitChosen.Should().BeTrue();
        window.StopRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Closing_while_an_update_runs_is_refused_and_says_why()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async IAsyncEnumerable<UpdateProgress> Held([EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new UpdateProgress(UpdateStage.Downloading, 1, 10, "Downloading");
            await gate.Task;
        }

        using var session = About(Model(), out var window, Held);

        window.StopRequested = false;

        var running = window.UpdateAsync(CancellationToken.None);

        window.InvokeCommand(Command.Quit);

        window.StopRequested.Should().BeFalse();
        window.ProgressText.Should().Contain(AboutWindow.StillUpdatingText);

        gate.SetResult();
        await running;
    }

    [Fact]
    public void Closing_with_no_update_running_closes()
    {
        using var session = About(Model(), out var window);

        window.StopRequested = false;

        window.InvokeCommand(Command.Quit);

        window.StopRequested.Should().BeTrue();
    }

    [Fact]
    public void Newer_release_shows_not_installed()
    {
        using var session = About(
            Model(releases: [Release("0.110.0", installed: false), Release("0.109.5", installed: true)]),
            out var window);

        var lines = window.TimelineLines;

        lines[0].Should().StartWith("0.110.0").And.EndWith("not installed");
        lines[1].Should().StartWith("0.109.5").And.NotContain("not installed");

        session.Screen.Should().Contain("not installed");
    }

    [Fact]
    public void Off_timeline_says_why()
    {
        using var session = About(Model(empty: TimelineEmpty.Off), out var window);

        var reason = Find<Label>(window, "about-timeline-empty");

        reason.Visible.Should().BeTrue();
        reason.Text.Should().Be(TimelineEmpty.Off.Describe());
    }

    [Fact]
    public void Guide_links_are_the_models()
    {
        const string Sentinel = "https://example.test/only-from-the-model";

        var opener = new RecordingOpener();

        using var session = About(
            Model(guides: [new AboutGuide("sentinel", "Sentinel guide", Sentinel)]),
            out var window,
            opener: opener);

        var guides = Find<ListView>(window, "about-guides");

        guides.SelectedItem = 0;
        guides.InvokeCommand(Command.Accept);

        opener.Opened.Should().Equal(Sentinel);
    }

    [Fact]
    public void A_guide_that_is_not_https_is_not_opened()
    {
        var opener = new RecordingOpener();

        using var session = About(
            Model(guides: [new AboutGuide("local", "Local", "file:///c:/windows/system32/calc.exe")]),
            out var window,
            opener: opener);

        var guides = Find<ListView>(window, "about-guides");

        guides.SelectedItem = 0;
        guides.InvokeCommand(Command.Accept);

        opener.Opened.Should().BeEmpty();
    }

    private sealed class RecordingOpener : IApplicationLauncher
    {
        public List<string> Opened { get; } = [];

        public Task<OperationResult> OpenInFileManagerAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> OpenUrlAsync(string url, CancellationToken ct = default)
        {
            Opened.Add(url);
            return Task.FromResult(OperationResult.Ok());
        }
    }
}

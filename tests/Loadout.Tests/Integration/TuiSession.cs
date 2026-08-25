using System.Drawing;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tests.Integration;

/// <summary>
/// Drives a screen the way a person does: keystrokes in, rendered text out.
/// <para>
/// The screens were only ever built and drawn before this. Nothing opened a
/// menu, moved focus or picked an item, and three defects reached a real
/// terminal through that gap — a crash on startup, a menu entry naming a
/// command that does not exist, and a capability that silently disappeared in a
/// rewrite. All three are the kind a keystroke would have caught and a
/// constructor call never could.
/// </para>
/// <para>
/// The ANSI driver is used rather than the platform one: it needs no real
/// terminal, behaves identically on all three operating systems, and can be
/// asked what it put on the screen.
/// </para>
/// </summary>
internal sealed class TuiSession : IDisposable
{
    /// <summary>
    /// A terminal wide enough that nothing under test is clipped. Tests that
    /// care about narrow terminals say so explicitly.
    /// </summary>
    internal const int DefaultWidth = 140;

    internal const int DefaultHeight = 40;

    private readonly IApplication _application;
    private readonly IInputInjector _injector;
    private readonly Runnable _window;

    /// <summary>The application the screen is running on.</summary>
    internal IApplication Application => _application;

    private TuiSession(IApplication application, Runnable window, int width, int height)
    {
        _application = application;
        _window = window;

        // Nothing draws without this. Off a real terminal the driver reports a
        // screen of no size, every view lays out to nothing, and assertions
        // about what is on screen quietly pass against an empty string.
        _application.Screen = new Rectangle(0, 0, width, height);

        _application.Begin(window);
        _application.LayoutAndDraw();

        _injector = _application.GetInputInjector();
    }

    /// <summary>
    /// Stands up a screen at a given size and draws it.
    /// </summary>
    /// <param name="build">Builds the screen, given the application it runs on.</param>
    /// <param name="width">Terminal columns.</param>
    /// <param name="height">Terminal rows.</param>
    internal static TuiSession Start(
        Func<IApplication, Runnable> build,
        int width = DefaultWidth,
        int height = DefaultHeight)
    {
        ArgumentNullException.ThrowIfNull(build);

        IApplication application = Terminal.Gui.App.Application.Create();

        application.Init(DriverRegistry.Names.ANSI);

        // Sized before the screen is built, because a view that lays out
        // against a zero-size screen caches the wrong bounds.
        application.Screen = new Rectangle(0, 0, width, height);

        return new TuiSession(application, build(application), width, height);
    }

    /// <summary>Presses one key and redraws.</summary>
    internal TuiSession Press(Key key)
    {
        _injector.InjectKey(key, new InputInjectionOptions());
        _injector.ProcessQueue();

        _application.RaiseIteration();
        _application.LayoutAndDraw();

        return this;
    }

    /// <summary>Types a run of characters, one key at a time.</summary>
    internal TuiSession Type(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var character in text)
        {
            Press(new Key(character));
        }

        return this;
    }

    /// <summary>
    /// Moves focus forward, as Tab does.
    /// </summary>
    /// <remarks>
    /// Use this rather than injecting <c>Key.Tab</c>. An injected Tab reaches
    /// the focused view as an ordinary keystroke and does not move focus, so a
    /// test that pressed it went on typing into the same field and then
    /// asserted against a selection that had never changed. Focus is moved
    /// through the navigation API, which is what the real Tab handler calls.
    /// </remarks>
    internal TuiSession Tab()
    {
        _application.Navigation?.AdvanceFocus(NavigationDirection.Forward, behavior: null);

        _application.LayoutAndDraw();

        return this;
    }

    /// <summary>What is on the screen right now.</summary>
    internal string Screen => _application.Driver?.ToString() ?? string.Empty;

    /// <summary>The view holding focus, or null when nothing does.</summary>
    internal View? Focused => _application.Navigation?.GetFocused();

    /// <summary>
    /// Redraws until the screen shows something, or patience runs out.
    /// </summary>
    /// <remarks>
    /// Some of what a screen displays is read off the main loop on purpose, so
    /// that a slow repository cannot freeze a list somebody is moving through.
    /// That makes the moment it appears genuinely asynchronous, and asserting
    /// immediately would be a race the test usually wins and occasionally
    /// loses.
    /// </remarks>
    internal string ScreenShowing(string expected, int timeoutMilliseconds = 5000)
    {
        ArgumentException.ThrowIfNullOrEmpty(expected);

        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        var screen = string.Empty;

        do
        {
            _application.RaiseIteration();
            _application.LayoutAndDraw();

            screen = Screen;

            if (screen.Contains(expected, StringComparison.Ordinal))
            {
                return screen;
            }

            Thread.Sleep(10);
        }
        while (Environment.TickCount64 < deadline);

        return screen;
    }

    public void Dispose()
    {
        _window.Dispose();
        _application.Dispose();
    }
}

using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// The opening screen: the name riding a wave while the first read finishes.
/// <para>
/// It is on screen for well under a second and any key ends it, because an
/// animation somebody cannot skip stops being a flourish the second time they
/// see it. It is also skipped outright where there is nobody watching — a
/// redirected run, or a terminal that cannot redraw — rather than spending a
/// second of a script's time on something no one will see.
/// </para>
/// </summary>
internal sealed class SplashScreen : Window
{
    /// <summary>
    /// How long the whole thing lasts. Long enough to read the name and see it
    /// move; short enough that somebody opening the launcher for the fortieth
    /// time today is not waiting on it.
    /// </summary>
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(900);

    /// <summary>Gap between frames. About sixteen frames over the duration.</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(55);

    private readonly IApplication _application;
    private readonly Label _wordmark;
    private readonly Label _status;
    private readonly object? _timer;

    private int _step;
    private int _framesLeft;

    internal SplashScreen(IApplication application, string status)
    {
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        Title = string.Empty;
        BorderStyle = LineStyle.None;

        _framesLeft = (int)(Duration.TotalMilliseconds / FrameInterval.TotalMilliseconds);

        _wordmark = new Label
        {
            X = Pos.Center(),
            Y = Pos.Center() - (Wordmark.Height / 2),
            Width = Wordmark.Width,
            Height = Wordmark.Height,
            Text = Wordmark.FrameText(0),
        };

        _status = new Label
        {
            X = Pos.Center(),
            Y = Pos.Bottom(_wordmark) + 1,
            Text = status,
        };

        Add(_wordmark, _status);

        // Any key at all, rather than a named one. Somebody reaching for the
        // keyboard during a splash screen wants it gone, and making them find
        // the right key to dismiss it defeats the point.
        this.Bind(Key.Esc, Command.Quit);
        this.Bind(Key.Enter, Command.Quit);
        this.Bind(Key.Space, Command.Quit);

        AddCommand(Command.Quit, () => { Finish(); return true; });

        _timer = _application.AddTimeout(FrameInterval, Advance);
    }

    /// <summary>Draws the next frame, and ends the screen after the last one.</summary>
    private bool Advance()
    {
        _step++;
        _framesLeft--;

        _wordmark.Text = Wordmark.FrameText(_step);

        if (_framesLeft <= 0)
        {
            Finish();

            // Stops the timer. Returning true here would keep it firing at a
            // screen that has already gone.
            return false;
        }

        return true;
    }

    private void Finish()
    {
        if (_timer is not null)
        {
            _application.RemoveTimeout(_timer);
        }

        _application.RequestStop(this);
    }

    /// <summary>
    /// Plays the animation, unless there is nobody to see it.
    /// </summary>
    /// <param name="application">The running application.</param>
    /// <param name="status">One line under the name, saying what is happening.</param>
    /// <param name="wanted">
    /// False for a terminal that cannot redraw, or a run nobody is watching.
    /// </param>
    internal static void Play(IApplication application, string status, bool wanted)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!wanted)
        {
            return;
        }

        using var splash = new SplashScreen(application, status);

        application.Run(splash);
    }
}

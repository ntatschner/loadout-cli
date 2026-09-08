using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// One line of keys and what they do, along the bottom of a screen, with
/// room to say something else instead for a moment.
/// </summary>
/// <remarks>
/// <para>
/// Built from labels rather than drawn, so the key and its meaning can be
/// different colours without a drawing routine that the headless tests
/// cannot see into. Each key is one label in the accent and each meaning
/// the label after it, so the line reads <c>Enter launch   Ctrl+P commands</c>
/// with the part you would press picked out. The toolkit's status bar draws
/// separators between its items and binds the keys itself, and both would
/// have to be undone: the keys here are already bound by the window.
/// </para>
/// <para>
/// A message put up with <see cref="Say"/> covers the keys until it is taken
/// back, which is what the bottom line did before this existed. It is the
/// one line on the screen that can change what somebody does next, so it
/// is not shared with anything that would compete for it.
/// </para>
/// </remarks>
internal sealed class KeyLine : View
{
    /// <summary>Blank columns between one hint and the next.</summary>
    private const int Gap = 3;

    private readonly List<View> _hints = [];
    private readonly Label _message;

    internal KeyLine(IReadOnlyList<(string Key, string Does)> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        Height = 1;
        Width = Dim.Fill();

        View? previous = null;

        foreach (var (key, does) in keys)
        {
            var pressed = new Label
            {
                X = previous is null ? 0 : Pos.Right(previous) + Gap,
                Y = 0,
                Text = key,
            };

            pressed.SetScheme(LauncherTheme.KeyOnGround);

            var meaning = new Label
            {
                X = Pos.Right(pressed) + 1,
                Y = 0,
                Text = does,
            };

            meaning.SetScheme(LauncherTheme.MutedOnGround);

            _hints.Add(pressed);
            _hints.Add(meaning);

            previous = meaning;
        }

        _message = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Visible = false,
        };

        Add([.. _hints, _message]);
    }

    /// <summary>What is being said instead of the keys, or null.</summary>
    internal string? Message => _message.Visible ? _message.Text : null;

    /// <summary>
    /// Says something in place of the keys. Null puts the keys back.
    /// </summary>
    internal void Say(string? message)
    {
        var saying = message is { Length: > 0 };

        _message.Text = message ?? string.Empty;
        _message.Visible = saying;

        foreach (var hint in _hints)
        {
            hint.Visible = !saying;
        }

        SetNeedsDraw();
    }
}

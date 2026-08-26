using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// A list that can be told what a key should do.
/// </summary>
/// <remarks>
/// <para>
/// Terminal.Gui offers three ways to give a view a key, and two of them do not
/// work from outside it. <c>AddCommand</c> is protected, so a view's commands
/// cannot be added by whoever built it. <c>KeyDown</c> does not fire for a
/// focused <see cref="ListView"/> at all. And a <see cref="KeyBinding"/> that
/// names another view as its target is accepted and then ignored: bound on the
/// list and answered by the window, the key does nothing whatever.
/// </para>
/// <para>
/// What does work is a plain binding to a command the list itself implements —
/// which is everything needed, once the list is a class that can implement one.
/// Hence this: three lines that make the supported mechanism reachable.
/// </para>
/// </remarks>
internal sealed class KeyedListView : ListView
{
    /// <summary>Makes a key run an action while this list has the focus.</summary>
    /// <param name="key">The key to bind.</param>
    /// <param name="command">
    /// The command to carry it on. Pick one the list does not already
    /// implement, or its own behaviour will be replaced rather than added to.
    /// </param>
    /// <param name="action">What the key does. Returns true when it acted.</param>
    internal void OnKey(Key key, Command command, Func<bool> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        this.Bind(key, command);

        AddCommand(command, _ => action());
    }
}

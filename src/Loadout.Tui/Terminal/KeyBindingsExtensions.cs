using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Loadout.Tui.Terminal;

/// <summary>
/// Binding a key without needing to know whether something already claimed it.
/// </summary>
internal static class KeyBindingsExtensions
{
    /// <summary>
    /// Binds a key to a command, replacing any existing binding for that key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>KeyBindings.Add</c> throws when the key is already bound, and several
    /// of the keys most worth binding are already bound by the view being
    /// derived from. Binding Enter on a window crashes for that reason:
    /// </para>
    /// <code>
    /// System.InvalidOperationException: A binding for Enter exists ([Quit], Key=Enter).
    /// </code>
    /// <para>
    /// Which is a crash on startup, in a real terminal, from a line that looks
    /// entirely unremarkable. Replacing is what every caller here wants anyway:
    /// a screen that binds a key has decided what that key does on it.
    /// </para>
    /// </remarks>
    internal static void Bind(this View view, Key key, Command command)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (view.KeyBindings.TryGet(key, out _))
        {
            view.KeyBindings.Remove(key);
        }

        view.KeyBindings.Add(key, command);
    }

    /// <summary>
    /// Binds a key for the whole application, so it fires wherever the focus is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A view's own bindings are not consulted while a child has the focus, and
    /// on a screen built from a list and a filter a child always has it. Ctrl+P
    /// and Ctrl+N were bound on the window, printed on its status line, and did
    /// nothing whatever: the list had the focus and had claimed both keys for
    /// extending a selection across a range.
    /// </para>
    /// <para>
    /// So a key the whole screen offers has to be bound for the whole
    /// application rather than for the view that happens to own it. Keys
    /// belonging to one view — j and k on a list, and every letter in the
    /// filter — stay bound where they are, which is the point of having both.
    /// </para>
    /// </remarks>
    internal static void BindEverywhere(this View view, Key key, Command command)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (view.KeyBindings.TryGet(key, out _))
        {
            view.KeyBindings.Remove(key);
        }

        view.KeyBindings.AddApp(key, view, command);
    }
}

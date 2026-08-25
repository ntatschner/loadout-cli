using Loadout.Tui;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// What a command is for, beyond the sentence that describes it.
/// <para>
/// Declared beside the command itself so there is one place to change it. The
/// alternative — a table of categories in the help text, another in the menu,
/// another in the documentation — is the arrangement that has already drifted
/// twice in this codebase, silently both times.
/// </para>
/// <para>
/// Only the category is required. The rest exists to answer questions the
/// launcher cannot otherwise answer: whether choosing this is going to change
/// anything, whether it needs the network, and what somebody might have typed
/// when looking for it.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CommandMetaAttribute : Attribute
{
    /// <param name="category">Which group this belongs to, from <see cref="CommandCategory"/>.</param>
    public CommandMetaAttribute(string category) => Category = category;

    /// <summary>Which group this belongs to.</summary>
    public string Category { get; }

    /// <summary>
    /// Words somebody might search for when they do not know the command's
    /// name, separated by spaces.
    /// </summary>
    /// <remarks>
    /// Nobody looking to undo a mistake searches for "backup restore"; they
    /// search for "undo". This is where that is written down.
    /// </remarks>
    public string Intent { get; init; } = string.Empty;

    /// <summary>Whether running this can change files or configuration.</summary>
    public bool Mutates { get; init; }

    /// <summary>Whether running this contacts the network.</summary>
    public bool RequiresNetwork { get; init; }

    /// <summary>One example of the command in use, without the leading name.</summary>
    public string Example { get; init; } = string.Empty;
}

using Loadout.Platform.Abstractions;

namespace Loadout.Core.Triggers;

/// <summary>
/// Whether triggers are on: the hidden switch they live behind until they are
/// whole.
/// </summary>
/// <remarks>
/// <para>
/// An environment variable rather than a setting, so tests and worktrees can
/// turn it on without touching anybody's configuration, and so removing it at
/// release is one small change rather than a migration.
/// </para>
/// <para>
/// The daemon reads it once, as it starts, and is the process whose answer
/// counts: it is the only one that acts on rules. Anything else asking should
/// ask the daemon, and fall back to this only when no daemon is running.
/// </para>
/// </remarks>
public static class TriggerSwitch
{
    /// <summary>The variable that turns triggers on.</summary>
    public const string Variable = "LOADOUT_TRIGGERS";

    /// <summary>Whether the variable says on: 1, true, on or yes, in any case.</summary>
    public static bool On(IEnvironmentProvider environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.GetVariable(Variable)?.Trim().ToLowerInvariant() is "1" or "true" or "on" or "yes";
    }

    /// <summary>What to say to somebody who asked for a trigger command while the switch is off.</summary>
    /// <remarks>
    /// Not 'loadout daemon restart': the running daemon starts its successor
    /// itself, which inherits its environment, so a variable set since in some
    /// other shell never reaches it.
    /// </remarks>
    public static string Refusal =>
        $"Triggers are not switched on. They are unfinished and live behind {Variable}: set it to 1, "
        + "then stop the daemon with 'loadout daemon stop' and start it again from that shell.";

    /// <summary>
    /// Whether triggers are on, asking the running daemon where there is one,
    /// and what to say when they are not.
    /// </summary>
    /// <param name="daemon">Whether a daemon is running, and if so whether it started with triggers on.</param>
    /// <param name="environment">This process's own environment, for when no daemon is running.</param>
    /// <remarks>
    /// The daemon's answer is the one that counts, because it is the only
    /// process that acts on rules. A shell with the variable set talking to a
    /// daemon started without it is told so, which explains a refusal that
    /// would otherwise make no sense to the person who just set it.
    /// </remarks>
    public static (bool On, string? Refusal) Ask(bool? daemon, IEnvironmentProvider environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var here = On(environment);

        return daemon switch
        {
            true => (true, null),
            false when here => (false,
                $"The running daemon was started without {Variable}, though it is set here. Stop it with "
                + "'loadout daemon stop' and start it again from this shell."),
            false => (false, Refusal),
            null => here ? (true, null) : (false, Refusal),
        };
    }
}

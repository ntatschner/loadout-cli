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
    public static string Refusal =>
        $"Triggers are not switched on. They are unfinished and live behind {Variable}: set it to 1 and "
        + "restart the daemon with 'loadout daemon restart' to try them.";
}

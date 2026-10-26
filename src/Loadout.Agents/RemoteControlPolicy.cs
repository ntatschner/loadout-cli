using Loadout.Models.Agents;

namespace Loadout.Agents;

/// <summary>What a launch does about the agent's remote control.</summary>
/// <param name="On">Whether the agent's switch is passed.</param>
/// <param name="Warning">Said when the default asked for it and the agent cannot.</param>
/// <param name="Refusal">Set when this session asked for it and the agent cannot.</param>
public sealed record RemoteControlDecision(bool On, string? Warning = null, string? Refusal = null);

/// <summary>
/// Settles whether a launch switches on the agent's own remote control.
/// </summary>
/// <remarks>
/// <para>
/// Loadout does no remoting. The agent does, and its data goes wherever that
/// agent sends it; the launcher passes one switch. That is why a machine-wide
/// default is allowed at all.
/// </para>
/// <para>
/// The two ways of asking are treated differently on purpose. A default set
/// once for every session should not stop a launch of an agent that never
/// had the feature, so it warns and starts without it. Asking on this launch
/// is a person saying they mean to reach this session from elsewhere, and
/// starting it unreachable would leave them finding that out away from the
/// machine, so it is refused.
/// </para>
/// </remarks>
public static class RemoteControlPolicy
{
    /// <param name="asked">The launch's own choice, or null to take the default.</param>
    /// <param name="byDefault">The machine's <c>remote-control</c> setting.</param>
    /// <param name="descriptor">What was detected of the agent.</param>
    /// <param name="headless">Whether this is a node driven without a terminal.</param>
    public static RemoteControlDecision Decide(
        bool? asked,
        bool byDefault,
        AgentDescriptor descriptor,
        bool headless)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // A node has no terminal for remote control to attach to; its
        // questions reach a person through the team's notices instead.
        if (headless || !(asked ?? byDefault))
        {
            return new RemoteControlDecision(false);
        }

        if (descriptor.Supports(AgentCapabilities.RemoteControl))
        {
            return new RemoteControlDecision(true);
        }

        var why = $"{descriptor.DisplayName} does not advertise remote control"
            + (descriptor.IsInstalled ? " on this build" : string.Empty);

        return asked is true
            ? new RemoteControlDecision(false, Refusal:
                $"Remote control was asked for, but {why}. Launch without --remote, or with an agent that has it.")
            : new RemoteControlDecision(false, Warning:
                $"Remote control is on by default, but {why}, so this session starts without it. "
                + "Turn the default off with 'loadout config set remote-control false', or pass --no-remote.");
    }
}

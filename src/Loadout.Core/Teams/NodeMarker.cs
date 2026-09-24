using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams;

/// <summary>How a process started as a node of a team run is told apart from a person's.</summary>
/// <remarks>
/// <para>
/// A node's tools are limited by patterns on the command it types, and a
/// pattern is a weak fence for a decision that belongs to a person: a lead
/// was once granted <c>Bash(loadout tools:*)</c> from the dashboard to run a
/// search, which also covered <c>loadout tools trust</c>. So every launch
/// that carries a node's permission policy also carries
/// <see cref="Variable" />, which the node's shell and everything it starts
/// inherit, and the commands that grant, trust or answer refuse when they
/// see it.
/// </para>
/// <para>
/// It keeps an honest node honest; it does not stop one that can run
/// arbitrary script, which could clear the variable first. A role with that
/// much reach is already trusted with more than these commands guard.
/// </para>
/// </remarks>
public static class NodeMarker
{
    /// <summary>Set on every node's process to the path of its permission policy.</summary>
    public const string Variable = "LOADOUT_TEAM_NODE";

    /// <summary>Why this process may not do something only a person decides, or null when it is not a node.</summary>
    /// <param name="environment">Where the variable is read from.</param>
    /// <param name="what">The decision, as a sentence subject: "Trusting a remedy".</param>
    public static string? Refusal(IEnvironmentProvider environment, string what)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.GetVariable(Variable) is { Length: > 0 }
            ? $"{what} is a person's decision, and this is a node of a team run. Nothing was changed. "
              + "Report what you need, and the person who started the run can do it."
            : null;
    }
}

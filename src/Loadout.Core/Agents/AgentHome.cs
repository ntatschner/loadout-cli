using Loadout.Platform.Abstractions;

namespace Loadout.Core.Agents;

/// <summary>
/// Where Claude Code keeps its own state on this machine.
/// </summary>
/// <remarks>
/// <para>
/// Six places needed this and two of them got it right. The memory importer and
/// project attribution honoured <c>CLAUDE_CONFIG_DIR</c>; the session list, the
/// usage history, the MCP reader and the status line installer each built
/// <c>~/.claude</c> by hand and did not. On a machine that has moved the
/// directory — which the agent supports and some people use to keep it off a
/// synced home — that meant <c>loadout usage</c> reported nothing, <c>sessions</c>
/// found nothing to resume, servers went unlisted, and <c>statusline install</c>
/// wrote to a settings file the agent does not read.
/// </para>
/// <para>
/// None of those failed loudly. They looked at a directory that was not there,
/// found nothing, and said so as though nothing were there to find, which is the
/// worst shape a wrong answer can take.
/// </para>
/// <para>
/// So there is one of these now, and the duplication that caused it is gone.
/// </para>
/// </remarks>
public static class AgentHome
{
    /// <summary>The variable the agent itself reads, so a moved directory is still found.</summary>
    public const string Override = "CLAUDE_CONFIG_DIR";

    /// <summary>The agent's configuration directory.</summary>
    public static string Claude(IEnvironmentProvider environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.GetVariable(Override) is { Length: > 0 } configured
            ? configured
            : Path.Combine(environment.HomeDirectory, ".claude");
    }

    /// <summary>Where the agent keeps per-repository state, one directory each.</summary>
    public static string ClaudeProjects(IEnvironmentProvider environment) =>
        Path.Combine(Claude(environment), "projects");

    /// <summary>The agent's settings file, which the status line is installed into.</summary>
    public static string ClaudeSettings(IEnvironmentProvider environment) =>
        Path.Combine(Claude(environment), "settings.json");
}

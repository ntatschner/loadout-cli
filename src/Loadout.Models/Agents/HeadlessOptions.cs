namespace Loadout.Models.Agents;

/// <summary>
/// What a headless session may do, in the launcher's vocabulary. Each
/// adapter translates it into its agent's own flags.
/// </summary>
/// <remarks>
/// <para>
/// A session with nobody at the keyboard has to be told everything up front:
/// how far it may go, what it may touch, who answers when it asks, and what
/// shape its answer must take. None of that is inherited from the machine's
/// interactive settings, because a node that picked up somebody's auto mode
/// or their global hooks would behave differently on every machine it ran
/// on, and the run could not say why.
/// </para>
/// </remarks>
/// <param name="Permission">How the agent's own permission checks are set for the session.</param>
/// <param name="AllowedTools">Tools and command patterns the agent may use without asking, in the agent's own spelling.</param>
/// <param name="DeniedTools">Tools and command patterns the agent may never use, in the agent's own spelling. Deny wins over allow.</param>
/// <param name="PermissionAnswerer">
/// The tool that answers when the agent would otherwise prompt, named as the
/// agent addresses it, or null to let anything that would prompt be denied.
/// </param>
/// <param name="MaxTurns">How many exchanges with the model the session may take before the agent stops it, or null for the agent's default.</param>
/// <param name="BudgetUsd">
/// What the session may spend before the agent stops it, or null for no cap.
/// The agent checks after each turn, so the turn that crosses the cap
/// completes; a caller enforcing a ceiling of its own allows for one turn of
/// overshoot.
/// </param>
/// <param name="OutputSchemaJson">A JSON Schema the agent's final answer must conform to, or null for free text.</param>
/// <param name="DisableHooks">Whether the agent's own hooks, from every settings scope, are switched off for the session.</param>
/// <param name="IsolateMcpServers">Whether only the MCP servers the launcher names are connected, rather than those plus the machine's own.</param>
public sealed record HeadlessOptions(
    HeadlessPermission Permission = HeadlessPermission.Ask,
    IReadOnlyList<string>? AllowedTools = null,
    IReadOnlyList<string>? DeniedTools = null,
    string? PermissionAnswerer = null,
    int? MaxTurns = null,
    decimal? BudgetUsd = null,
    string? OutputSchemaJson = null,
    bool DisableHooks = true,
    bool IsolateMcpServers = true);

/// <summary>How a headless session's permission checks are set.</summary>
public enum HeadlessPermission
{
    /// <summary>The agent's default: anything not on its allow list is asked, and the answerer decides.</summary>
    Ask,

    /// <summary>File edits are accepted without asking; everything else is asked.</summary>
    AcceptEdits,

    /// <summary>Nothing is asked: what the allow list names runs, everything else is denied.</summary>
    DenyUnlessAllowed,

    /// <summary>
    /// The agent's checks are off. Deny lists still hold. Reachable only by a
    /// machine-local decision the person makes for a named role, never from a
    /// shared file; the guard is the caller's, and the adapter emits what it
    /// is given.
    /// </summary>
    Bypass,
}

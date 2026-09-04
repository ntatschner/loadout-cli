namespace Loadout.Models.Policies;

/// <summary>
/// Commands pre-approved on this machine, so the agent stops asking about them.
/// </summary>
/// <remarks>
/// <para>
/// Machine-local by construction. This lives in <c>config.yaml</c>, which is
/// not in the workspace and is never committed or synced, and that placement is
/// the whole point rather than an implementation detail: pre-approval removes
/// an approval prompt, and a file that travels between people is a file that
/// can remove somebody else's. Denial travels with the project instead, in a
/// security profile, because tightening is safe to share.
/// </para>
/// <para>
/// Keyed by project slug so pre-approving <c>npm test</c> where that is
/// obviously fine does not pre-approve it everywhere. A denial in the project's
/// profile always wins, so nothing here can put back something the project took
/// away.
/// </para>
/// </remarks>
public sealed class CommandPolicySettings
{
    /// <summary>Project slug to the commands pre-approved for it on this machine.</summary>
    public Dictionary<string, List<string>> PreApproved { get; set; } = [];
}

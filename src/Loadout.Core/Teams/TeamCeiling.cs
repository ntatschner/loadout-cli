namespace Loadout.Core.Teams;

/// <summary>
/// What this machine lets a team allow its nodes, whatever the team file asks
/// for.
/// </summary>
/// <remarks>
/// <para>
/// The trust boundary for team runs, and the same split as everywhere else
/// here: the shared half proposes and the local half decides. A team file lives
/// in a workspace anybody on the team can push to, so a list of outward actions
/// in one is a request. This is the answer.
/// </para>
/// <para>
/// Written as a pure function over two lists because that is the whole of the
/// decision, and because a boundary with two implementations has one that
/// drifts. The shipped <c>release-crew</c> asks to push a tag in an autonomous
/// run, which is precisely the kind of thing a machine should have to agree to
/// before it happens with nobody watching.
/// </para>
/// </remarks>
public static class TeamCeiling
{
    /// <summary>What a team asked for, split into what it gets and what it does not.</summary>
    /// <param name="Allowed">Named by the team and granted by this machine.</param>
    /// <param name="Refused">Named by the team and not granted here.</param>
    public sealed record Decision(IReadOnlyList<string> Allowed, IReadOnlyList<string> Refused)
    {
        /// <summary>Whether anything the team asked for is not on offer here.</summary>
        public bool IsShort => Refused.Count > 0;
    }

    /// <summary>Nothing asked for, so nothing to decide.</summary>
    public static readonly Decision Nothing = new([], []);

    /// <summary>
    /// Decides what a team's outward list comes to on this machine.
    /// </summary>
    /// <param name="asked">
    /// What the team file names, which is a request and never a grant.
    /// </param>
    /// <param name="granted">
    /// What this machine's own configuration allows a team to allow. Empty by
    /// default, which is the answer that keeps a fresh machine from pushing
    /// anything because a file somebody else edited said it could.
    /// </param>
    public static Decision Decide(
        IReadOnlyList<string>? asked,
        IReadOnlyList<string>? granted)
    {
        if (asked is not { Count: > 0 })
        {
            return Nothing;
        }

        var allowed = new List<string>();
        var refused = new List<string>();

        foreach (var action in asked)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                continue;
            }

            // Exactly, not by prefix. "git push" granted must not carry
            // "git push --force" with it, and a machine that meant to allow
            // both can say both.
            if ((granted ?? []).Any(one => string.Equals(one.Trim(), action.Trim(), StringComparison.Ordinal)))
            {
                allowed.Add(action.Trim());
            }
            else
            {
                refused.Add(action.Trim());
            }
        }

        return new Decision(allowed, refused);
    }

    /// <summary>
    /// How to say a refusal to somebody, naming what to do about it.
    /// </summary>
    /// <remarks>
    /// The reason a run is refused up front rather than narrowed quietly: a
    /// narrowed run spends money for several minutes and then fails at the one
    /// step it was started for, and says the node could not push rather than
    /// that this machine never let it.
    /// </remarks>
    public static string Explain(string team, Decision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return $"Team '{team}' allows its nodes {Listed(decision.Refused)} in an autonomous run, "
            + "and this machine does not let a team allow that. A team file is shared and may only ask. "
            + "To agree to it here: loadout config set team-outward-allowed "
            + $"\"{string.Join(", ", decision.Refused)}\" "
            + "— or run it with --autonomy supervised, where the list is ignored and you are asked.";
    }

    private static string Listed(IReadOnlyList<string> actions) =>
        string.Join(", ", actions.Select(action => $"'{action}'"));
}

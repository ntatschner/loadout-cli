using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>
/// Whether a remediator may run a remedy without asking.
/// </summary>
/// <remarks>
/// <para>
/// Two keys, and neither of them alone. A person at this machine trusts a
/// particular script; this machine's own configuration says what a kind of task
/// may do. A remedy that is trusted but whose kind is held is held; a kind that
/// may run unattended does not carry an untrusted script with it.
/// </para>
/// <para>
/// The same shape as <see cref="TeamCeiling" /> and for the same reason: the
/// half that can be written by an agent proposes, and the half that lives on
/// this machine decides. An agent can register a remedy, improve it, and say it
/// is wonderful. It cannot trust it.
/// </para>
/// <para>
/// A pure function over what is already known, because a decision with two
/// implementations has one that drifts, and this is the one that decides
/// whether a script runs on somebody's machine with nobody watching.
/// </para>
/// </remarks>
public static class RemedyCeiling
{
    /// <summary>What was decided, and the sentence saying why.</summary>
    /// <param name="Ruling">Run it, ask about it, or refuse it.</param>
    /// <param name="Because">
    /// Why, in words a person reads on the request. Never a code: somebody
    /// looking at a held remediation is deciding, and "refused" without a
    /// reason is not something anybody can act on.
    /// </param>
    public sealed record Decision(RemedyRuling Ruling, string Because);

    /// <summary>
    /// Decides what a remediator may do with one remedy on this machine.
    /// </summary>
    /// <param name="remedy">The remedy, as its record in the team's directory has it.</param>
    /// <param name="rule">
    /// What this machine says about that kind of task, or null where it has
    /// said nothing - which is asking.
    /// </param>
    /// <param name="script">
    /// The script as it is now, so trust granted to one version is not spent on
    /// another. Null skips the comparison, which is for a caller that has
    /// already made it.
    /// </param>
    public static Decision Decide(Remedy? remedy, string? rule, string? script = null)
    {
        if (remedy is null)
        {
            return new Decision(RemedyRuling.Refuse, "There is no remedy by that name in this team's directory.");
        }

        var said = (rule ?? RemedyRules.Default).Trim().ToLowerInvariant();

        // A machine that has said never means never, and nothing about the
        // remedy changes it. Checked first so that a trusted remedy of a
        // refused kind is refused rather than run.
        if (string.Equals(said, RemedyRules.Never, StringComparison.Ordinal))
        {
            return new Decision(
                RemedyRuling.Refuse,
                $"This machine refuses remediation of kind '{Named(remedy.Kind)}' outright.");
        }

        if (!remedy.IsTrusted)
        {
            return new Decision(
                RemedyRuling.Ask,
                "Nobody has trusted this remedy on this machine.");
        }

        // Trust was granted to a script, not to a name. The declaration that
        // tells a team to keep improving what it registers is exactly the
        // thing that would otherwise spend one remedy's trust on another.
        if (script is not null && !Matches(remedy, script))
        {
            return new Decision(
                RemedyRuling.Ask,
                "This remedy has changed since it was trusted.");
        }

        if (!string.Equals(said, RemedyRules.Trusted, StringComparison.Ordinal))
        {
            return new Decision(
                RemedyRuling.Ask,
                $"This machine holds every remediation of kind '{Named(remedy.Kind)}' for a person.");
        }

        return new Decision(
            RemedyRuling.Run,
            $"Trusted here, and this machine lets a trusted '{Named(remedy.Kind)}' remedy run.");
    }

    /// <summary>Whether a script is the one that was trusted.</summary>
    /// <remarks>
    /// A remedy with no fingerprint was trusted before there was one to record,
    /// or by something that did not record it. Treated as not matching, because
    /// the safe reading of "I cannot tell whether this is what you agreed to"
    /// is to ask.
    /// </remarks>
    public static bool Matches(Remedy remedy, string script)
    {
        ArgumentNullException.ThrowIfNull(remedy);
        ArgumentNullException.ThrowIfNull(script);

        return remedy.Fingerprint is { Length: > 0 } kept
            && string.Equals(kept, Fingerprint(script), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The fingerprint of a script, as it is recorded.</summary>
    /// <remarks>
    /// Over the bytes as written, with line endings normalised, because a file
    /// that crossed between Windows and anything else is the same script and a
    /// person who trusted it would say so.
    /// </remarks>
    public static string Fingerprint(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var normalised = script.Replace("\r\n", "\n", StringComparison.Ordinal);

        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(normalised)))
            .ToLowerInvariant();
    }

    /// <summary>A kind, or a word for having none.</summary>
    private static string Named(string kind) =>
        kind is { Length: > 0 } named ? named.Trim() : "unclassified";
}

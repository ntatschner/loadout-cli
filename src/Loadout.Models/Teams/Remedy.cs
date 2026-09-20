namespace Loadout.Models.Teams;

/// <summary>
/// Something a team worked out how to fix, written down so it can be run
/// again.
/// </summary>
/// <remarks>
/// <para>
/// A team that investigates a problem and fixes it has learned something, and
/// until now that something lived in a transcript. A remedy is the same fix as
/// a script or a function, registered in the team's directory with enough
/// beside it that a person can decide whether to let it run without being
/// asked again.
/// </para>
/// <para>
/// Mutable with settable properties because it deserialises from YAML in the
/// team's directory, like a team file and a project manifest.
/// </para>
/// <para>
/// Nothing a node writes here grants anything. A remedy arrives untrusted, and
/// only a person at this machine changes that — the same split as everywhere
/// else in teams: the agent proposes, the machine decides.
/// </para>
/// </remarks>
public sealed class Remedy
{
    /// <summary>The name it is run and asked for by. Lowercase, hyphenated.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What sort of task this is: <c>disk</c>, <c>service</c>, <c>network</c>,
    /// whatever a team uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The thing a machine sets a rule against. "May a remediator restart a
    /// service unattended" and "may it delete files off a full disk" are
    /// different questions with different answers, and a single switch for
    /// "automated remediation" cannot tell them apart.
    /// </para>
    /// <para>
    /// Free text rather than a fixed list, because the kinds worth
    /// distinguishing are the ones a particular system has. A kind the machine
    /// has said nothing about is asked about, every time.
    /// </para>
    /// </remarks>
    public string Kind { get; set; } = string.Empty;

    /// <summary>What it does, in a sentence somebody deciding can act on.</summary>
    public string What { get; set; } = string.Empty;

    /// <summary>What it assumes about the machine it runs on.</summary>
    /// <remarks>
    /// The half of a script that decides whether running it is safe here as
    /// opposed to where it was written. A remedy that assumes a directory
    /// exists and is run where it does not is the ordinary way one of these
    /// goes wrong.
    /// </remarks>
    public string Assumes { get; set; } = string.Empty;

    /// <summary>How somebody would know it worked.</summary>
    public string Proves { get; set; } = string.Empty;

    /// <summary>The file in the team's directory, relative to it.</summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>The run that registered it.</summary>
    public string RegisteredBy { get; set; } = string.Empty;

    /// <summary>When it was registered.</summary>
    public DateTimeOffset? RegisteredAt { get; set; }

    /// <summary>How many times it has been improved since.</summary>
    public int Revision { get; set; }

    /// <summary>
    /// Whether a person at this machine has said a remediator may run it:
    /// <c>untrusted</c> or <c>trusted</c>.
    /// </summary>
    /// <remarks>
    /// Untrusted on arrival and untrusted after every change: a remedy that
    /// kept its trust through an edit would be a remedy whose trust was granted
    /// to something else. See <see cref="Fingerprint" />.
    /// </remarks>
    public string Trust { get; set; } = Untrusted;

    /// <summary>Who trusted it, in their own words, or empty.</summary>
    public string TrustedBy { get; set; } = string.Empty;

    /// <summary>When it was trusted.</summary>
    public DateTimeOffset? TrustedAt { get; set; }

    /// <summary>
    /// The script as it was when it was trusted.
    /// </summary>
    /// <remarks>
    /// Trust is granted to a particular script, not to a name. Without this, a
    /// node could register something, have it trusted, and then improve it into
    /// something nobody agreed to — which is exactly what the declaration
    /// telling it to keep improving them asks it to do.
    /// </remarks>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Not trusted: a remediator may not run it unasked.</summary>
    public const string Untrusted = "untrusted";

    /// <summary>Trusted: a remediator may run it where the machine allows the kind.</summary>
    public const string Trusted = "trusted";

    /// <summary>Whether a person has trusted this exact script.</summary>
    public bool IsTrusted =>
        string.Equals(Trust, Trusted, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a machine lets a remediator do with a kind of task.</summary>
/// <remarks>
/// <para>
/// The machine's half of the decision. A remedy's trust is the other half, and
/// neither alone lets anything run unattended: the same two-key shape as the
/// outward actions a team may allow, and for the same reason.
/// </para>
/// </remarks>
public static class RemedyRules
{
    /// <summary>Never, whatever anybody says about the remedy.</summary>
    public const string Never = "never";

    /// <summary>Hold it for a person, every time, trusted or not.</summary>
    public const string Ask = "ask";

    /// <summary>
    /// Run it without asking, but only where a person has trusted that exact
    /// script.
    /// </summary>
    public const string Trusted = "trusted";

    /// <summary>What a kind nobody has written a rule for comes to.</summary>
    /// <remarks>
    /// Asking, rather than refusing. Refusing an unknown kind outright reads as
    /// safe and is not: a remediator that cannot ask is one that stops, and a
    /// person who is never asked never learns the kind exists to write a rule
    /// for it.
    /// </remarks>
    public const string Default = Ask;

    /// <summary>Every rule, in the order they are documented.</summary>
    public static IReadOnlyList<string> All { get; } = [Never, Ask, Trusted];
}

/// <summary>What happens when a remediator asks to run a remedy.</summary>
public enum RemedyRuling
{
    /// <summary>Held for a person. The ordinary answer.</summary>
    Ask,

    /// <summary>Run it, unattended.</summary>
    Run,

    /// <summary>Refused, and not by asking.</summary>
    Refuse,
}

namespace Loadout.Models.Instructions;

/// <summary>
/// Which layer of the composition a specialist belongs to.
/// </summary>
/// <remarks>
/// <para>
/// A closed set on purpose, and the one part of this system that is
/// architecture rather than content. The order these are declared in is the
/// order their instructions are composed in, running from the most general to
/// the most specific, so that where two of them touch the same subject the
/// narrower one is read last.
/// </para>
/// <para>
/// Specialists themselves are data and grow without touching code. Kinds are
/// not: adding one changes what "more specific" means. An enum also makes an
/// unrecognised kind fail at load, which is the behaviour wanted — a
/// misspelled kind that silently became a general rule would be worse than a
/// refusal.
/// </para>
/// </remarks>
public enum SpecialistKind
{
    /// <summary>Durable engineering rules that apply whatever the task.</summary>
    Foundation,

    /// <summary>Posture: advising, investigating, implementing or reviewing.</summary>
    Mode,

    /// <summary>A programming language.</summary>
    Language,

    /// <summary>A framework or runtime, sitting on top of a language.</summary>
    Framework,

    /// <summary>A database engine.</summary>
    Database,

    /// <summary>An operating system, container runtime or orchestrator.</summary>
    Platform,

    /// <summary>A cloud provider.</summary>
    Cloud,

    /// <summary>A cross-cutting engineering specialty such as security or performance.</summary>
    Function,

    /// <summary>A repeatable procedure rather than a body of expertise.</summary>
    Skill,
}

/// <summary>Where a specialist was loaded from, which decides who can override whom.</summary>
public enum SpecialistOrigin
{
    /// <summary>Shipped inside the launcher.</summary>
    BuiltIn,

    /// <summary>
    /// From a specialist pack fetched from a Git remote and approved here.
    /// </summary>
    /// <remarks>
    /// Between the built-ins and the workspace on purpose. A pack is house
    /// standards from elsewhere, and the workspace and the project are this
    /// team's and this project's own — whatever they say has to win, or
    /// adopting a pack would quietly overrule decisions somebody made
    /// deliberately.
    /// </remarks>
    Pack,

    /// <summary>Written in the central workspace, shared across machines.</summary>
    Workspace,

    /// <summary>Written under one project in the workspace.</summary>
    Project,
}

/// <summary>
/// What causes a specialist to be considered for a task.
/// </summary>
/// <remarks>
/// <para>
/// This is the part the proposed library did not have. Its manifest recorded
/// which <em>mechanisms</em> were permitted to activate each specialist —
/// explicit, preference, repository, task — and every one of the fifty-two
/// entries carried the same four values. That says nothing about what the
/// evidence actually is, so no resolver could have been written against it.
/// </para>
/// <para>
/// Everything here is evidence rather than instruction. Matching raises a
/// specialist as a candidate with a reason attached; it never obliges the
/// resolver to load it. A repository with one <c>.sql</c> file in it must not
/// turn every task into a database task.
/// </para>
/// </remarks>
/// <param name="Always">
/// Loaded whatever the task. True only for foundation material, which is kept
/// deliberately small: everything here is paid for on every single launch.
/// </param>
/// <param name="Globs">
/// Repository paths that suggest this specialist, matched with the same glob
/// engine path-scoped rules already use.
/// </param>
/// <param name="Dependencies">
/// Substrings of a declared dependency that suggest it, such as
/// <c>Npgsql</c> or <c>@types/react</c>. Matched against manifests rather than
/// against source, because a package reference is a much stronger signal than a
/// file extension.
/// </param>
/// <param name="TaskPhrases">
/// Words in the task that suggest it. The strongest signal available, and the
/// only one that reflects what somebody is actually trying to do.
/// </param>
/// <param name="Requires">
/// Other specialists that must be present for this one to make sense, such as a
/// framework needing its language. Kept shallow and explicit: composition, not
/// an inheritance tree.
/// </param>
/// <param name="Capabilities">
/// Agent capabilities this specialist needs before it is worth loading. Empty
/// for almost everything, because instructions are text.
/// </param>
/// <param name="Command">
/// What a skill is offered as when it is also a command, or <c>none</c> where
/// it is not offered as one. Only meaningful on a skill. Absent means the
/// command takes its name from the id, which is what nearly all of them want;
/// this is for the few where the id reads well in a listing and badly as
/// something somebody types.
/// </param>
/// <param name="Modes">
/// Modes this specialist applies to. Empty means all of them.
/// </param>
public sealed record SpecialistActivation(
    bool Always = false,
    IReadOnlyList<string>? Globs = null,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyList<string>? TaskPhrases = null,
    IReadOnlyList<string>? Requires = null,
    IReadOnlyList<string>? Capabilities = null,
    IReadOnlyList<string>? Modes = null,
    string? Command = null)
{
    public static readonly SpecialistActivation None = new();

    public IReadOnlyList<string> GlobList => Globs ?? [];

    public IReadOnlyList<string> DependencyList => Dependencies ?? [];

    public IReadOnlyList<string> TaskPhraseList => TaskPhrases ?? [];

    public IReadOnlyList<string> RequiresList => Requires ?? [];

    public IReadOnlyList<string> CapabilityList => Capabilities ?? [];

    public IReadOnlyList<string> ModeList => Modes ?? [];
}

/// <summary>
/// Something a session doing as this specialist asks would visibly do.
/// </summary>
/// <remarks>
/// <para>
/// Loading a specialist is not the same as following it, and until now only the
/// first was measurable: <c>instructions stats</c> says which ones launches
/// reached and nothing at all about whether any of them changed what happened
/// next. A specialist nobody follows costs tokens on every launch and buys
/// nothing, and there was no way to find that out.
/// </para>
/// <para>
/// A probe is one observable consequence, not a proof of compliance. It can
/// say a session did the thing, never that it did it because of this. Most
/// specialists have no probe and should not be given a contrived one: a
/// signature nobody can write is a specialist whose effect cannot be measured,
/// and inventing one would put a number on a question it does not answer.
/// </para>
/// </remarks>
/// <param name="Summary">What the signature is, in the words of a person.</param>
/// <param name="Tools">Tool names whose use counts, as the agent records them.</param>
/// <param name="Argument">
/// An argument that must be set on the call. Null when any use of the tool
/// counts.
/// </param>
/// <param name="Pattern">
/// A regular expression the call's arguments must match. Null when the tool
/// alone is the signature.
/// </param>
/// <param name="Absent">
/// That the signature is the tool <em>not</em> being used.
/// </param>
/// <remarks>
/// Some guidance asks for restraint rather than an act, and the only thing it
/// leaves behind is the absence of something. Asking the person a question is
/// the case this was added for: the rule is to carry on and say which reading
/// was taken, so the session that followed it has no question in its transcript
/// at all.
/// <para>
/// It changes who is counted as well as what counts. A positive signature is
/// measured against the sessions that used the tool, because those are the ones
/// that had the chance to use it well; an absent one is measured against every
/// session that did any work, because any of them could have stopped and asked.
/// </para>
/// </remarks>
public sealed record SpecialistProbe(
    string Summary,
    IReadOnlyList<string> Tools,
    string? Argument = null,
    string? Pattern = null,
    bool Absent = false);

/// <summary>
/// One specialist: a body of guidance plus the evidence that makes it relevant.
/// </summary>
/// <remarks>
/// <para>
/// Self-describing, so the library is what is on disk rather than what a
/// manifest claims is on disk. The proposed design kept activation in a
/// registry file and prose in separate markdown, which makes three failures
/// possible that this shape cannot have: the registry naming a file that does
/// not exist, the two drifting apart, and a path in the registry pointing
/// somewhere it should not.
/// </para>
/// </remarks>
/// <param name="Id">
/// Stable dotted identifier, such as <c>language.csharp</c>. The first segment
/// matches the kind, which is checked rather than assumed — an id that says one
/// thing while the kind says another is a mistake somebody will act on.
/// </param>
/// <param name="Kind">Which layer it composes into.</param>
/// <param name="Title">Human-facing name.</param>
/// <param name="Summary">One line, shown in listings and in the explanation.</param>
/// <param name="Activation">What makes it relevant.</param>
/// <param name="Body">The guidance itself, everything after the frontmatter.</param>
/// <param name="Bytes">Size of the body in UTF-8, which is what it costs.</param>
/// <param name="Origin">Where it came from.</param>
/// <param name="Path">
/// Where it was read from, for diagnostics. Empty for built-ins, which are not
/// on disk at all.
/// </param>
/// <param name="Probe">
/// Something a session following this would visibly do, or null where no
/// honest signature can be written. Most are null.
/// </param>
public sealed record SpecialistDocument(
    string Id,
    SpecialistKind Kind,
    string Title,
    string Summary,
    SpecialistActivation Activation,
    string Body,
    long Bytes,
    SpecialistOrigin Origin = SpecialistOrigin.BuiltIn,
    string Path = "",
    SpecialistProbe? Probe = null)
{
    /// <summary>
    /// A rough token count for the body.
    /// </summary>
    /// <remarks>
    /// Four bytes to the token, which is the usual approximation for English
    /// prose. Deliberately called an estimate everywhere it is shown: no
    /// tokeniser here matches the ones the providers actually use, and a figure
    /// presented as exact would be believed. Bytes remain the authoritative
    /// measure because they are the one this can actually know.
    /// </remarks>
    public int EstimatedTokens => (int)Math.Ceiling(Bytes / 4.0);

    /// <summary>The segment of the id after the kind, e.g. <c>csharp</c>.</summary>
    public string Name
    {
        get
        {
            var dot = Id.IndexOf('.', StringComparison.Ordinal);

            return dot >= 0 && dot + 1 < Id.Length ? Id[(dot + 1)..] : Id;
        }
    }
}

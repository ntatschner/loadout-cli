namespace Loadout.Core.Triggers;

/// <summary>Something that happened, as the spine carries it from a source to the rules.</summary>
/// <param name="Id">Its own name, which every decision about it in the journal points at.</param>
/// <param name="At">When it happened, or when its source saw it.</param>
/// <param name="Source">What produced it, such as <c>daemon</c> or <c>webhook</c>.</param>
/// <param name="Kind">What happened: one of <see cref="TriggerEventKinds.All"/>.</param>
/// <param name="Subject">
/// What it is about, such as one run or one project. A burst of events about
/// one subject becomes one action on the latest of them.
/// </param>
/// <param name="Project">The project it happened in, where it happened in one.</param>
/// <param name="Fields">What a rule's conditions can ask about, by name.</param>
/// <param name="Origins">
/// The rules whose actions led to it, oldest first, so a rule is never started
/// again by what it started itself.
/// </param>
public sealed record TriggerEvent(
    string Id,
    DateTimeOffset At,
    string Source,
    string Kind,
    string Subject,
    string? Project,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> Origins)
{
    /// <summary>A field's value, or null where the event does not carry it.</summary>
    public string? Field(string name) =>
        Fields.TryGetValue(name, out var value) ? value : null;
}

/// <summary>The kinds of event the spine knows, and what each one carries.</summary>
/// <remarks>
/// Known in advance rather than whatever a source happens to send, so a rule
/// asking for a field no event of its kind carries is refused when it is
/// written instead of never matching, silently, for ever.
/// </remarks>
public static class TriggerEventKinds
{
    /// <summary>A team run reached its end.</summary>
    public const string RunFinished = "run-finished";

    /// <summary>A project's documentation no longer matches what it describes.</summary>
    public const string DocsDrift = "docs-drift";

    /// <summary>Something outside this machine asked, through <c>/api/trigger</c>.</summary>
    public const string Webhook = "webhook";

    /// <summary>
    /// A rule's own clock came round: every so often, or once a day at a time,
    /// what a schedule does.
    /// </summary>
    /// <remarks>
    /// Each clock event belongs to one rule, named in its <c>rule</c> field,
    /// and only that rule is asked about it.
    /// </remarks>
    public const string Clock = "clock";

    /// <summary>A pull request was opened.</summary>
    public const string PrOpened = "pr-opened";

    /// <summary>A pull request's branch moved: somebody pushed to it.</summary>
    public const string PrUpdated = "pr-updated";

    /// <summary>A draft pull request was marked ready for review.</summary>
    public const string PrReady = "pr-ready";

    /// <summary>A pull request that was ready for review went back to being a draft.</summary>
    public const string PrDraft = "pr-draft";

    /// <summary>A pull request now merges into a different base branch.</summary>
    public const string PrRetargeted = "pr-retargeted";

    /// <summary>A pull request was closed without being merged.</summary>
    public const string PrClosed = "pr-closed";

    /// <summary>A pull request was merged.</summary>
    public const string PrMerged = "pr-merged";

    /// <summary>A closed pull request was opened again, which makes it a fresh subject.</summary>
    public const string PrReopened = "pr-reopened";

    /// <summary>A GitHub Actions run failed, on a pull request's branch or another.</summary>
    public const string CiFailed = "ci-failed";

    /// <summary>An issue was opened.</summary>
    public const string IssueOpened = "issue-opened";

    /// <summary>Somebody commented on an issue or a pull request.</summary>
    public const string Comment = "comment";

    /// <summary>Every kind, in the order the commands list them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        RunFinished, DocsDrift, Webhook, Clock,
        PrOpened, PrUpdated, PrReady, PrDraft, PrRetargeted, PrClosed, PrMerged, PrReopened,
        CiFailed, IssueOpened, Comment,
    ];

    /// <summary>The kinds that end their subject: nothing waiting on it is worth starting afterwards.</summary>
    public static readonly IReadOnlyList<string> Ending = [PrClosed, PrMerged];

    /// <summary>Whether a kind comes from GitHub, and so carries an author and a trust.</summary>
    public static bool FromGitHub(string? kind) =>
        kind is { Length: > 0 } && GitHubKinds.Contains(kind, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> GitHubKinds =
    [
        PrOpened, PrUpdated, PrReady, PrDraft, PrRetargeted, PrClosed, PrMerged, PrReopened,
        CiFailed, IssueOpened, Comment,
    ];

    /// <summary>What every pull request event carries.</summary>
    /// <remarks>
    /// <c>trust</c> is <c>trusted</c> or <c>outside</c>, and <c>because</c>
    /// says which association or allow-list entry decided it, so the journal
    /// can say why a run was trusted. <c>state</c> is <c>open</c>,
    /// <c>closed</c> or <c>merged</c>.
    /// </remarks>
    private static readonly IReadOnlyList<string> Pull =
        ["repo", "number", "base", "branch", "head", "author", "author-id", "trust", "because", "draft", "state", "title", "url"];

    /// <summary>The fields each kind carries, which are the ones a rule may ask about.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Fields =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [RunFinished] = ["team", "project", "outcome", "run"],
            [DocsDrift] = ["project", "path", "count"],
            [Webhook] = ["team", "project", "goal"],
            [Clock] = ["rule"],
            [PrOpened] = Pull,
            [PrUpdated] = Pull,
            [PrReady] = Pull,
            [PrDraft] = Pull,
            [PrRetargeted] = [.. Pull, "was-base"],
            [PrClosed] = Pull,
            [PrMerged] = Pull,
            [PrReopened] = [.. Pull, "was"],
            [CiFailed] = [.. Pull, "workflow", "run"],
            [IssueOpened] = ["repo", "number", "author", "author-id", "trust", "because", "title", "url"],
            [Comment] = ["repo", "number", "on", "author", "author-id", "trust", "because", "mention", "draft", "state", "url"],
        };

    /// <summary>Whether a kind is one the spine knows.</summary>
    public static bool Known(string? kind) =>
        kind is { Length: > 0 } && Fields.ContainsKey(kind);
}

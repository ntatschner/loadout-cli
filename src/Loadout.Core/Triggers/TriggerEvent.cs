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

    /// <summary>Every kind, in the order the commands list them.</summary>
    public static readonly IReadOnlyList<string> All = [RunFinished, DocsDrift, Webhook];

    /// <summary>The fields each kind carries, which are the ones a rule may ask about.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Fields =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [RunFinished] = ["team", "project", "outcome", "run"],
            [DocsDrift] = ["project", "path"],
            [Webhook] = ["team", "project", "goal"],
        };

    /// <summary>Whether a kind is one the spine knows.</summary>
    public static bool Known(string? kind) =>
        kind is { Length: > 0 } && Fields.ContainsKey(kind);
}

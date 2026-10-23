namespace Loadout.Core.Triggers;

/// <summary>
/// What a new event about a subject does to the actions already waiting on it.
/// </summary>
/// <remarks>
/// <para>
/// One rule, applied whatever kind the new event is: an action waiting on a
/// subject is checked against the subject as it is now. A push moves the head
/// on, and a review of the old head is not worth starting. A close or merge
/// ends the subject. Going back to draft, or a new base, can stop a rule
/// matching. Whatever no longer holds is withdrawn and written down.
/// </para>
/// <para>
/// Trust is the exception. A new event never raises the trust of one already
/// waiting: an action goes ahead with the trust it had when it was decided,
/// and a raised trust arrives with the next event. Lowering trust is done at
/// once, by the allow-list command, not here.
/// </para>
/// </remarks>
public static class TriggerSubjects
{
    /// <summary>
    /// The field a newly opened pull request carries naming the branch subject
    /// it takes over. Not one a rule can ask about.
    /// </summary>
    public const string FoldsField = "folds";

    /// <summary>The fields that describe where a subject stands, rather than who made it.</summary>
    private static readonly IReadOnlyList<string> State = ["draft", "state", "base"];

    /// <summary>The fields that describe a pull request, which a branch event takes on when it folds into one.</summary>
    private static readonly IReadOnlyList<string> Pull = ["number", "base", "author", "author-id", "trust", "because", "draft", "state", "title"];

    /// <summary>Why a waiting event is no longer worth acting on now that <paramref name="newer"/> has arrived, or null where it still is.</summary>
    /// <remarks>Only the reasons that hold whatever the rule; whether the rule still matches is the caller's check.</remarks>
    public static string? Overtaken(TriggerEvent waiting, TriggerEvent newer)
    {
        ArgumentNullException.ThrowIfNull(waiting);
        ArgumentNullException.ThrowIfNull(newer);

        if (TriggerEventKinds.Ending.Contains(newer.Kind, StringComparer.OrdinalIgnoreCase))
        {
            return $"the pull request was {newer.Field("state") ?? "closed"}";
        }

        if (waiting.Field("head") is { Length: > 0 } was && newer.Field("head") is { Length: > 0 } now
            && !string.Equals(was, now, StringComparison.Ordinal))
        {
            return $"the branch moved on to {now[..Math.Min(7, now.Length)]}";
        }

        return null;
    }

    /// <summary>
    /// A waiting event as it would look with its subject's state as it is now,
    /// for checking against its rule. Never acted on: the waiting event keeps
    /// its own fields, trust above all.
    /// </summary>
    public static TriggerEvent Refreshed(TriggerEvent waiting, TriggerEvent newer)
    {
        ArgumentNullException.ThrowIfNull(waiting);
        ArgumentNullException.ThrowIfNull(newer);

        var fields = new Dictionary<string, string>(waiting.Fields, StringComparer.OrdinalIgnoreCase);

        foreach (var name in State)
        {
            if (newer.Field(name) is { } value)
            {
                fields[name] = value;
            }
        }

        return waiting with { Fields = fields };
    }

    /// <summary>
    /// A branch event moved onto the pull request opened from its branch: the
    /// same failure, now about the pull request, with its author and trust.
    /// </summary>
    /// <remarks>
    /// The pull request's trust replaces the branch's, which can lower it: a
    /// branch pushed by a collaborator can be opened as a pull request by
    /// somebody outside. The fold is checked against the rule again for that.
    /// </remarks>
    public static TriggerEvent Folded(TriggerEvent waiting, TriggerEvent opened)
    {
        ArgumentNullException.ThrowIfNull(waiting);
        ArgumentNullException.ThrowIfNull(opened);

        var fields = new Dictionary<string, string>(waiting.Fields, StringComparer.OrdinalIgnoreCase);

        foreach (var name in Pull)
        {
            if (opened.Field(name) is { } value)
            {
                fields[name] = value;
            }
        }

        return waiting with
        {
            Id = $"{waiting.Id}>{opened.Subject}",
            Subject = opened.Subject,
            Fields = fields,
        };
    }
}

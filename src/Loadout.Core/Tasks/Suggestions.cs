using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>Where a suggested reply came from.</summary>
public enum SuggestionSource
{
    /// <summary>
    /// Built from the recorded state, so it cannot be wrong about what it names.
    /// </summary>
    /// <remarks>
    /// "continue add-the-widget" is composed: the task exists, it is in that
    /// state, and the sentence was assembled from those facts. There is no step
    /// at which it could have become untrue.
    /// </remarks>
    Composed,

    /// <summary>Written by an agent, and therefore capable of being wrong.</summary>
    /// <remarks>
    /// A drafted suggestion can name a task that does not exist or a state that
    /// changed an hour ago, confidently and in the same shape as a composed
    /// one. Saying which is which is the entire safety of the feature.
    /// </remarks>
    Drafted,
}

/// <summary>A short reply somebody can accept instead of composing one.</summary>
/// <param name="Text">The reply.</param>
/// <param name="Source">Where it came from, which is never inferred.</param>
public sealed record Suggestion(string Text, SuggestionSource Source);

/// <summary>
/// Builds short replies out of what is recorded.
/// </summary>
/// <remarks>
/// <para>
/// Composed and drafted suggestions are never mixed into one list. A composed
/// reply cannot be wrong about the state it names because it was assembled from
/// that state; a drafted one can be confidently wrong about exactly the same
/// thing, and the only defence anybody has is being told which they are looking
/// at. Blending them would take that away in the name of a tidier list.
/// </para>
/// <para>
/// Nothing here performs anything. Offering an action and taking it are
/// different features, and only the first was asked for.
/// </para>
/// </remarks>
public static class Suggestions
{
    /// <summary>
    /// How many to offer.
    /// </summary>
    /// <remarks>
    /// A short list is the feature. Thirty suggestions is a backlog with a
    /// different name, and reading it costs more than typing the reply would
    /// have.
    /// </remarks>
    public const int Most = 5;

    /// <summary>Replies built from the recorded state.</summary>
    /// <param name="tasks">What is recorded.</param>
    /// <param name="unsupported">What the repository did not back up.</param>
    public static IReadOnlyList<Suggestion> Compose(
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyList<TaskDisagreement>? unsupported = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var offered = new List<Suggestion>();

        void Offer(string text)
        {
            if (offered.Count < Most
                && !offered.Any(s => string.Equals(s.Text, text, StringComparison.OrdinalIgnoreCase)))
            {
                offered.Add(new Suggestion(text, SuggestionSource.Composed));
            }
        }

        // In the order somebody would act on them. What is underway comes
        // before what is stuck, which comes before what has not started — a
        // list that opened with the untouched backlog would be answering a
        // question nobody asked mid-session.
        foreach (var task in Ordered(tasks, TaskState.Doing))
        {
            Offer($"continue {task.Id}");
        }

        foreach (var task in Ordered(tasks, TaskState.Blocked))
        {
            Offer($"why is {task.Id} blocked");
        }

        foreach (var disagreement in unsupported ?? [])
        {
            // Deliberately "check", not "fix". The record not backing a claim
            // up is not the same as the claim being wrong, and a suggestion
            // that said "fix" would decide that question on nobody's authority.
            Offer($"check {disagreement.TaskId}");
        }

        foreach (var task in Ordered(tasks, TaskState.Open))
        {
            Offer($"start {task.Id}");
        }

        return offered;
    }

    /// <summary>
    /// Marks a reply an agent wrote as one an agent wrote.
    /// </summary>
    /// <remarks>
    /// The only way to make a drafted suggestion, and it takes the text
    /// unchanged. Nothing here inspects or improves it: the point is the label,
    /// and a method that also edited the text would be a place for a drafted
    /// reply to quietly become something else.
    /// </remarks>
    public static Suggestion Draft(string text) =>
        new(text?.Trim() ?? string.Empty, SuggestionSource.Drafted);

    private static IEnumerable<TaskItem> Ordered(IReadOnlyList<TaskItem> tasks, TaskState state) =>
        tasks
            .Where(task => task.State == state && task.Id.Length > 0)
            .OrderByDescending(task => task.DeclaredUtc)
            .ThenBy(task => task.Id, StringComparer.Ordinal);
}

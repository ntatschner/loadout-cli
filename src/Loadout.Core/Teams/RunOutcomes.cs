using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>
/// Which category a run's ending falls into.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and separate from both the journal and the commands, because this
/// decides what a prune takes. Getting it wrong deletes the wrong run, and
/// the journal is the only copy.
/// </para>
/// <para>
/// A run written by a current build carries its category, settled when it
/// ended. Everything already on this machine carries only the sentence, so
/// the sentence is read as well — the endings a run can write are a closed
/// set, listed in TeamRunner, and each one is covered by a test.
/// </para>
/// <para>
/// An ending nothing here recognises answers null rather than guessing.
/// A filter that matched a sentence it did not understand would delete runs
/// on a coincidence, and the caller asked for a category, not a best effort.
/// </para>
/// </remarks>
public static class RunOutcomes
{
    /// <summary>The category this run ended in, or null where it cannot be said.</summary>
    public static RunOutcome? Of(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Running)
        {
            return RunOutcome.Running;
        }

        // What the run itself decided, where it was built to say. Read first
        // so that changing the wording below never re-categorises a run that
        // has already ended.
        if (run.Outcome is { Length: > 0 } recorded && TryParse(recorded, out var written))
        {
            return written;
        }

        return From(run.Ended);
    }

    /// <summary>The category an ending sentence describes, or null for one nothing here knows.</summary>
    public static RunOutcome? From(string? ended)
    {
        if (string.IsNullOrWhiteSpace(ended))
        {
            // A finish with no word about how it went. A run that was killed
            // does not reach here at all: it never writes a finish, so it
            // reads as still going.
            return RunOutcome.Unrecorded;
        }

        var said = ended.Trim();

        // The four the lead reports as its own status, word for word.
        if (Same(said, "done"))
        {
            return RunOutcome.Done;
        }

        if (Same(said, "blocked"))
        {
            return RunOutcome.Blocked;
        }

        if (Same(said, "needs-decision"))
        {
            return RunOutcome.NeedsDecision;
        }

        if (Same(said, "failed"))
        {
            return RunOutcome.Failed;
        }

        if (Starts(said, "round limit:") || Starts(said, "budget spent:"))
        {
            return RunOutcome.Limited;
        }

        // "stopped by request", "stopped by the person", "stopped at a
        // decision", "stopped before the lead was briefed".
        if (Starts(said, "stopped"))
        {
            return RunOutcome.Stopped;
        }

        // A run halted for taking an outward action its brief did not allow
        // did not deliver, and is filed with the rest of what did not. The
        // sentence beside it is what says it was a refusal rather than a
        // fault.
        if (Starts(said, "halted:")
            || Starts(said, "no progress:")
            || Starts(said, "the lead could not be started")
            || Starts(said, "the lead ended without a report")
            // "reported done without accounting for the goal", and "reported
            // done with N of M criteria unmet". A done the lead could not
            // account for is not a done - that is the whole point of asking
            // it to account.
            || Starts(said, "the lead reported done"))
        {
            return RunOutcome.Failed;
        }

        return null;
    }

    /// <summary>The word a category is written and typed as.</summary>
    public static string Spell(RunOutcome outcome) => outcome switch
    {
        RunOutcome.NeedsDecision => "needs-decision",
        _ => outcome.ToString().ToLowerInvariant(),
    };

    /// <summary>Reads a category back from the word somebody typed.</summary>
    public static bool TryParse(string? word, out RunOutcome outcome)
    {
        outcome = default;

        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        var asked = word.Trim();

        foreach (var known in Enum.GetValues<RunOutcome>())
        {
            if (Same(asked, Spell(known)))
            {
                outcome = known;

                return true;
            }
        }

        return false;
    }

    /// <summary>Every category somebody may ask for, in the order they are listed to them.</summary>
    public static IReadOnlyList<string> Names =>
        [.. Enum.GetValues<RunOutcome>().Select(Spell)];

    private static bool Same(string said, string word) =>
        string.Equals(said, word, StringComparison.OrdinalIgnoreCase);

    private static bool Starts(string said, string opening) =>
        said.StartsWith(opening, StringComparison.OrdinalIgnoreCase);
}

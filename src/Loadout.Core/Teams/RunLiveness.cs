namespace Loadout.Core.Teams;

/// <summary>Whether anything is still driving a run.</summary>
public abstract record Liveness
{
    private Liveness()
    {
    }

    /// <summary>The run wrote its finish.</summary>
    public sealed record Finished : Liveness;

    /// <summary>Something is driving it, or it is too soon to say otherwise.</summary>
    public sealed record Live : Liveness;

    /// <summary>The run had an owner and that owner has gone.</summary>
    /// <param name="LastOwner">Who last drove it, where the record could be read.</param>
    public sealed record NotResponding(RunOwner? LastOwner) : Liveness;

    /// <summary>
    /// The run never recorded an owner and has written nothing for a long
    /// while. It may be a run from an older build that is still going.
    /// </summary>
    /// <param name="Quiet">How long since it last wrote anything.</param>
    public sealed record PossiblyNotResponding(TimeSpan Quiet) : Liveness;
}

/// <summary>
/// Reads whether a run is still driven, from its journal and its owner's lock.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and beside the journal's fold rather than in it: the fold
/// reads only what the run wrote, and liveness needs the lock and the clock,
/// which the caller reads and passes in.
/// </para>
/// <para>
/// A run with no owner record can only be judged by how quiet it has been,
/// and that is a guess, so it is never more than "possibly" and only after
/// <see cref="OldRunQuiet"/>. A lock that was taken and is free is not a
/// guess: the process that held it has ended.
/// </para>
/// </remarks>
public static class RunLiveness
{
    /// <summary>
    /// How long a run with no owner record must have been quiet before it is
    /// said to be possibly not responding.
    /// </summary>
    /// <remarks>
    /// Applies only to runs with no record. A lead's turn can run for many
    /// minutes without a word in the journal, so anything much shorter would
    /// flag runs that are working.
    /// </remarks>
    public static readonly TimeSpan OldRunQuiet = TimeSpan.FromHours(1);

    /// <summary>Whether the run is still driven.</summary>
    /// <param name="run">The run as its journal reads.</param>
    /// <param name="owner">What asking about its owner found.</param>
    /// <param name="journalWritten">When its journal file was last written, since <see cref="RunSummary.LastSeen"/> counts node events only.</param>
    /// <param name="now">The time to judge quiet against.</param>
    public static Liveness Of(RunSummary run, OwnerReading owner, DateTimeOffset journalWritten, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(owner);

        if (!run.Running)
        {
            return new Liveness.Finished();
        }

        switch (owner)
        {
            case OwnerReading.Held:
                return new Liveness.Live();

            case OwnerReading.Free free:
                return new Liveness.NotResponding(free.LastOwner);
        }

        var lastWrote = run.LastSeen > journalWritten ? run.LastSeen : journalWritten;
        var quiet = now - lastWrote;

        return quiet >= OldRunQuiet
            ? new Liveness.PossiblyNotResponding(quiet)
            : new Liveness.Live();
    }
}

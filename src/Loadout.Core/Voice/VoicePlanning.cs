using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>What a run expects to spend.</summary>
/// <param name="Calls">Model calls.</param>
/// <param name="Usd">Estimated cost.</param>
/// <param name="PerCallUsd">What each call is assumed to cost.</param>
/// <param name="FromHistory">Whether the per-call figure came from earlier runs rather than the guide's seed.</param>
public sealed record VoiceEstimate(int Calls, decimal Usd, decimal PerCallUsd, bool FromHistory);

/// <summary>Which question, if any, the cap needs before a run spends anything.</summary>
public enum VoiceCapQuestion
{
    /// <summary>The stored cap covers the estimate and is not far above it.</summary>
    None,

    /// <summary>There is no cap yet: the first run asks.</summary>
    First,

    /// <summary>The estimate has grown past the stored cap.</summary>
    Larger,

    /// <summary>The estimate has shrunk so far that the stored cap no longer means anything.</summary>
    Lower,
}

/// <summary>How much of a rule's evidence still counts.</summary>
/// <param name="Citations">Distinct pieces of writing cited that still hold.</param>
/// <param name="Sources">Distinct kinds of writing among them.</param>
/// <param name="Meets">Whether that reaches the guide's threshold.</param>
public sealed record VoiceEvidenceCount(int Citations, int Sources, bool Meets);

/// <summary>
/// The arithmetic of a learn run: how many calls it needs, what that is
/// expected to cost, whether the cap needs a question, and whether a rule has
/// the evidence to be proposed.
/// </summary>
/// <remarks>
/// Kept apart from the run itself so every threshold can be tested without a
/// model, and so the numbers a person is asked to approve come from one place.
/// </remarks>
public static class VoicePlanning
{
    /// <summary>
    /// The writing in batches of about the guide's size, in order. A piece is
    /// never split, so one larger than a batch has a batch to itself.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<VoiceEvidence>> Batches(IReadOnlyList<VoiceEvidence> evidence, VoiceGuide guide)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(guide);

        var batches = new List<IReadOnlyList<VoiceEvidence>>();
        var current = new List<VoiceEvidence>();
        var size = 0;

        foreach (var piece in evidence)
        {
            if (current.Count > 0 && size + piece.Text.Length > guide.Cap.BatchCharacters)
            {
                batches.Add(current);
                current = [];
                size = 0;
            }

            current.Add(piece);
            size += piece.Text.Length;
        }

        if (current.Count > 0)
        {
            batches.Add(current);
        }

        return batches;
    }

    /// <summary>
    /// What a run of so many calls is expected to cost: the average of the
    /// calls already made, or the guide's seed before there are any.
    /// </summary>
    public static VoiceEstimate Estimate(int calls, VoiceLearnState state, VoiceGuide guide)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(guide);

        var made = state.Runs.Sum(r => r.Calls);
        var spent = state.Runs.Sum(r => r.CostUsd);
        var fromHistory = made > 0 && spent > 0;
        var perCall = fromHistory ? Math.Round(spent / made, 4) : guide.Cap.SeedCallUsd;

        return new VoiceEstimate(calls, calls * perCall, perCall, fromHistory);
    }

    /// <summary>The estimate plus the guide's margin, as a cap.</summary>
    public static VoiceCap Proposed(VoiceEstimate estimate, VoiceGuide guide, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(guide);

        var factor = 1 + (decimal)guide.Cap.Margin;

        return new VoiceCap
        {
            Calls = (int)Math.Ceiling(estimate.Calls * factor),
            Usd = Math.Round(estimate.Usd * factor, 2, MidpointRounding.AwayFromZero),
            Estimate = estimate.Usd,
            Set = now,
        };
    }

    /// <summary>
    /// A cap from a figure the person typed. The calls follow from it at the
    /// estimate's per-call cost, and never fall below one, since a cap of no
    /// calls is a decline.
    /// </summary>
    public static VoiceCap Typed(decimal usd, VoiceEstimate estimate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(estimate);

        return new VoiceCap
        {
            Calls = Math.Max(1, estimate.PerCallUsd > 0 ? (int)Math.Floor(usd / estimate.PerCallUsd) : estimate.Calls),
            Usd = usd,
            Estimate = estimate.Usd,
            Set = now,
        };
    }

    /// <summary>
    /// Whether the cap needs a question before anything is spent.
    /// </summary>
    /// <remarks>
    /// A larger estimate asks before mining rather than letting the run end
    /// partial, because it could have been avoided. A lower one asks only when
    /// the proposal is below the guide's fraction of the stored cap; between
    /// that and the cap, a cap stays higher than it needs to be without
    /// anybody being asked, which is the price of not asking every time.
    /// </remarks>
    public static VoiceCapQuestion Question(VoiceCap? stored, VoiceEstimate estimate, VoiceGuide guide)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(guide);

        if (stored is null)
        {
            return VoiceCapQuestion.First;
        }

        if (estimate.Usd > stored.Usd || estimate.Calls > stored.Calls)
        {
            return VoiceCapQuestion.Larger;
        }

        var proposed = Proposed(estimate, guide, DateTimeOffset.MinValue);

        return proposed.Usd < stored.Usd * (decimal)guide.Cap.LowerBelow
            ? VoiceCapQuestion.Lower
            : VoiceCapQuestion.None;
    }

    /// <summary>Whether another call would go past the cap. The check is before the call, so a run can overshoot by one call's cost at most.</summary>
    public static bool Reached(VoiceCap cap, int calls, decimal spent)
    {
        ArgumentNullException.ThrowIfNull(cap);

        return calls >= cap.Calls || spent >= cap.Usd;
    }

    /// <summary>
    /// How much of a rule's evidence still counts: citations whose text is as
    /// it was when cited. An interview answer is the person's own word and is
    /// not mined evidence, so it never counts here.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <param name="guide">Whose threshold.</param>
    /// <param name="holding">Every piece of writing that currently counts, as its reference and hash.</param>
    public static VoiceEvidenceCount Count(VoiceRule rule, VoiceGuide guide, ISet<(string Reference, string Hash)> holding)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(guide);
        ArgumentNullException.ThrowIfNull(holding);

        var counted = rule.Citations
            .Where(c => c.Hash is { } hash && holding.Contains((c.Reference, hash)))
            .ToList();

        var citations = counted.Select(c => c.Reference).Distinct(StringComparer.Ordinal).Count();
        var sources = counted.Select(c => c.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return new VoiceEvidenceCount(
            citations, sources, citations >= guide.Evidence.Citations && sources >= guide.Evidence.Sources);
    }

    /// <summary>The writing that counts now, as a set to check citations against.</summary>
    public static ISet<(string Reference, string Hash)> Holding(IEnumerable<VoiceEvidence> evidence) =>
        evidence.Select(e => (e.Reference, e.Hash)).ToHashSet();
}

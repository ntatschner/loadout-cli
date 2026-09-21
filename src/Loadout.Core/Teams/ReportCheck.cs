using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>What the coordinator does with a report after reading it.</summary>
public enum ReportOutcome
{
    /// <summary>The report is taken as it stands.</summary>
    Accepted,

    /// <summary>The report goes back to the node once, with the reasons, for a corrected one.</summary>
    Returned,

    /// <summary>The run pauses and the person is told. The node did something the brief did not allow.</summary>
    Halted,
}

/// <summary>The coordinator's judgement of a report, with its reasons.</summary>
/// <param name="Outcome">What happens next.</param>
/// <param name="Reasons">Why, one sentence each, written so they can be sent back to the node as they are.</param>
public sealed record ReportVerdict(ReportOutcome Outcome, IReadOnlyList<string> Reasons)
{
    /// <summary>A report with nothing wrong with it.</summary>
    public static ReportVerdict Accepted { get; } = new(ReportOutcome.Accepted, []);
}

/// <summary>
/// The rules the schema cannot express, applied to a report before anyone
/// acts on it.
/// </summary>
/// <remarks>
/// <para>
/// A schema can say a status is one of four words; it cannot say that
/// <c>done</c> needs evidence that passed. These are those rules, and they
/// are the whole of the consequence the roles describe: a report that fails
/// them is returned to the node once, and a report that took an outward
/// action the brief did not allow pauses the run.
/// </para>
/// <para>
/// Deterministic and explainable, like the specialist resolver. Nothing here
/// consults a model, and every reason is a sentence the node can act on.
/// </para>
/// </remarks>
public static class ReportCheck
{
    /// <summary>Judges a report against the brief that produced it.</summary>
    public static ReportVerdict Check(Report report, Brief brief)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(brief);

        // Outward actions first, because they decide the outcome on their
        // own: whatever else is wrong with the report, an action the brief
        // did not allow is not something to send back for correction.
        var allowed = new HashSet<string>(brief.Constraints.OutwardAllowed ?? [], StringComparer.Ordinal);
        var unallowed = report.OutwardTaken.Where(action => !allowed.Contains(action)).ToList();

        if (unallowed.Count > 0)
        {
            return new ReportVerdict(
                ReportOutcome.Halted,
                unallowed.Select(action =>
                    $"The node reports taking the outward action '{action}', which the brief did not allow.")
                    .ToList());
        }

        var reasons = new List<string>();

        if (!string.Equals(report.Node, brief.Node, StringComparison.Ordinal))
        {
            reasons.Add($"The report says it is from '{report.Node}', and the brief was for '{brief.Node}'.");
        }

        switch (report.Status)
        {
            case ReportStatus.Done:
                if (!report.Evidence.Any(evidence => evidence.Result == EvidenceResult.Pass))
                {
                    reasons.Add(
                        "Status done needs at least one evidence entry whose result is pass. Work believed "
                        + "finished but not verified is blocked, with unblocked_by saying what verification needs.");
                }

                if (brief.Deliverable != DeliverableKind.Answer && report.Deliverables.Count == 0)
                {
                    reasons.Add(
                        $"Status done needs at least one deliverable, and the brief asked for a {Spell(brief.Deliverable)}.");
                }

                break;

            case ReportStatus.Blocked:
                if (report.Blocker is null)
                {
                    reasons.Add("Status blocked needs a blocker: what is blocking, and what would unblock it.");
                }

                break;

            case ReportStatus.NeedsDecision:
                if (report.Questions is not { Count: > 0 })
                {
                    reasons.Add("Status needs-decision needs at least one question, with options and a recommendation.");
                }

                break;

            case ReportStatus.Failed:
                if (string.IsNullOrWhiteSpace(report.Summary))
                {
                    reasons.Add("Status failed needs a summary saying what was tried.");
                }

                break;
        }

        return reasons.Count == 0
            ? ReportVerdict.Accepted
            : new ReportVerdict(ReportOutcome.Returned, reasons);
    }

    private static string Spell(DeliverableKind kind) => kind.ToString().ToLowerInvariant();
}

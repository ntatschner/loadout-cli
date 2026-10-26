using System.Globalization;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>
/// A person saying a criterion a run handed them is met, written into the
/// run's journal as review evidence in their name.
/// </summary>
/// <remarks>
/// <para>
/// Only for a run that handed over, and only for what it handed over: a
/// criterion the run said it could meet itself is the run's to meet, and a
/// person ticking it here would be the claim with nothing behind it that the
/// criteria exist to stop.
/// </para>
/// <para>
/// Each check is its own <c>criterion.checked</c> event, so the log says who
/// checked what and when, and the run is judged again from the journal
/// rather than from anything kept beside it.
/// </para>
/// </remarks>
public static class RunHandoverChecks
{
    /// <summary>
    /// The handed-over criteria named by what somebody typed: a number as the
    /// handover lists them, or the criterion's own words.
    /// </summary>
    public static OperationResult<IReadOnlyList<HandoverItem>> Pick(RunSummary run, IReadOnlyList<string> picked)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(picked);

        if (Refusal(run) is { } refused)
        {
            return Refuse(refused);
        }

        var handover = run.Handover!;

        if (picked.Count == 0)
        {
            return Refuse($"Say which to check, by number: {Listed(handover)}.");
        }

        var chosen = new List<HandoverItem>();

        foreach (var one in picked)
        {
            var said = one.Trim();

            var item = int.TryParse(said, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number >= 1 && number <= handover.Items.Count
                    ? handover.Items[number - 1]
                    : handover.Items.FirstOrDefault(each => string.Equals(each.Criterion.Trim(), said, StringComparison.OrdinalIgnoreCase));

            if (item is null)
            {
                return Refuse($"'{said}' is not one it handed over. It handed over: {Listed(handover)}.");
            }

            if (item.Checked)
            {
                return Refuse($"'{item.Criterion}' was already checked by {item.CheckedBy}.");
            }

            if (!chosen.Contains(item))
            {
                chosen.Add(item);
            }
        }

        return OperationResult<IReadOnlyList<HandoverItem>>.Ok(chosen);
    }

    /// <summary>Why a run has no handover to check, or null where it has one.</summary>
    public static string? Refusal(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Running)
        {
            return $"Run {run.RunId} is still going, so it has handed nothing over yet.";
        }

        if (!RunHandovers.IsHandover(run.Ended) || run.Handover is not { Items.Count: > 0 })
        {
            return $"Run {run.RunId} did not hand anything over: it ended {run.Ended ?? "without saying how"}. "
                + "Only a criterion a run handed to you can be checked here.";
        }

        return null;
    }

    /// <summary>
    /// Writes each check into the run's journal and hands back the handover as
    /// it reads afterwards.
    /// </summary>
    /// <param name="journal">Where the run is.</param>
    /// <param name="runId">The run.</param>
    /// <param name="picked">Which, as <see cref="Pick"/> reads them.</param>
    /// <param name="by">Who checked them.</param>
    /// <param name="at">When.</param>
    public static OperationResult<RunHandover> Record(
        IRunJournal journal,
        string runId,
        IReadOnlyList<string> picked,
        string by,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(journal);

        IReadOnlyList<HandoverItem> chosen = [];

        var written = RunJournalAppend.Append(journal, runId, at, run =>
        {
            var pick = Pick(run, picked);

            if (pick.Failed)
            {
                return OperationResult<IReadOnlyList<RunJournalAppend.Line>>.Fail(pick.Error!, pick.ExitCode);
            }

            chosen = pick.Value!;

            return OperationResult<IReadOnlyList<RunJournalAppend.Line>>.Ok(
            [
                .. chosen.Select(item => new RunJournalAppend.Line("criterion.checked", new
                {
                    criterion = item.Criterion,
                    by,
                    evidence = new { kind = "review", result = "pass", @ref = $"checked by hand by {by}", note = item.ToDo },
                })),
            ]);
        });

        if (written.Failed)
        {
            return OperationResult<RunHandover>.Fail(written.Error!, written.ExitCode);
        }

        return OperationResult<RunHandover>.Ok(Ticked(written.Value!.Handover!, chosen, by, at));
    }

    /// <summary>The handover with these ticked, as it will read once they are written.</summary>
    public static RunHandover Ticked(RunHandover handover, IReadOnlyList<HandoverItem> chosen, string by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(handover);
        ArgumentNullException.ThrowIfNull(chosen);

        return handover with
        {
            Items = [.. handover.Items.Select(one => chosen.Contains(one) ? one with { CheckedBy = by, CheckedAt = at } : one)],
        };
    }

    private static string Listed(RunHandover handover) =>
        string.Join("; ", handover.Items.Select((one, i) => $"{i + 1}. {one.Criterion}{(one.Checked ? " (checked)" : string.Empty)}"));

    private static OperationResult<IReadOnlyList<HandoverItem>> Refuse(string why) =>
        OperationResult<IReadOnlyList<HandoverItem>>.Fail(why, ExitCode.InvalidArguments);
}

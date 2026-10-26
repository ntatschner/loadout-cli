using Loadout.Core.Teams;
using Loadout.Models.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// What a lead turn came to: the run's ending, a reminder that sends the lead
/// round again, or a report to act on.
/// </summary>
internal sealed record LeadTurn(string? Ended, bool Reminded, Report? Report);

/// <summary>
/// One turn of the lead: its report judged, the criteria agreed on its first,
/// its readings written down, the gate reminder, and whether its status ends
/// the run.
/// </summary>
internal sealed class LeadReportStep
{
    private readonly NodeSessions _sessions;
    private readonly IntegrateStep _integrate;

    public LeadReportStep(NodeSessions sessions, IntegrateStep integrate)
    {
        _sessions = sessions;
        _integrate = integrate;
    }

    public async Task<LeadTurn> RunAsync(RunContext run, JournalWriter journal, CancellationToken ct)
    {
        var (team, console, autonomy, standing) = (run.Team, run.Console, run.Autonomy, run.Standing);

        var turn = await _sessions.TurnAsync(run.Lead!, run.LeadBrief, run.Prompt, journal, ct).ConfigureAwait(false);
        var report = turn.Report;
        run.Cost += turn.Cost;

        if (turn.Halted)
        {
            run.Ended = "halted: the lead took an outward action its brief did not allow";
            run.Final = report;
            return new LeadTurn(run.Ended, false, null);
        }

        if (report is null)
        {
            // What it said instead is the explanation, and it is
            // usually a sentence a person can act on.
            run.Ended = turn.Said.Length > 0
                ? $"the lead ended without a report. It said: {turn.Said}"
                : "the lead ended without a report";

            return new LeadTurn(run.Ended, false, null);
        }

        run.Final = report;

        /*
          What this run is judged on, agreed before a worker is briefed.

          Only where nobody said: a run started with --done-when was
          settled by whoever typed them. Without them the run's only
          criterion was "the goal is met", which the lead writes its own
          verdict on - so a run that produced a design document for a
          goal somebody wanted code from reported done, cited itself,
          and was right by the only rule it had.

          Here rather than after the dispatch, because the workers
          briefed in this same round are handed the criteria too, and a
          criterion agreed after they have gone is one they never saw.
        */
        if (run.Criteria is null && !run.Proposed)
        {
            run.Proposed = true;

            run.Criteria = await TeamRunner.AgreedAsync(report, autonomy, console, journal, standing, ct)
                .ConfigureAwait(false);

            if (run.Criteria is { Count: > 0 })
            {
                // The brief is what ReportCheck reads, so every later
                // done is held to these exactly as it would be to ones
                // somebody typed.
                run.LeadBrief = run.LeadBrief with
                {
                    Criteria = TeamRunner.Held(standing, run.Criteria),
                    DoneWhen =
                    [
                        "every criterion below is met, with the evidence cited from your "
                            + "nodes' reports",
                        TeamRunner.CoverageOwed,
                        TeamRunner.ReadingsOwed,
                        TeamRunner.GoalReadingOwed,
                    ],
                };

                // All of it, the project's included: this is the list
                // the lead's coverage is checked against.
                run.AgreedThisRound = TeamRunner.Held(standing, run.Criteria);
            }
        }

        // Written down the first time and whenever one changes, so a
        // run read back shows how the lead understood what it was
        // judged on before the verdicts, not only beside them.
        var held = TeamRunner.Held(standing, run.Criteria);
        var tags = TeamRunner.Tagged(report, held);

        if (TeamRunner.Read(report, held) is { Count: > 0 } readings)
        {
            // A tag that moved is a change too: it decides whether the run
            // works on a criterion or hands it over.
            var changed = run.LastReadings is null
                ? readings
                : readings
                    .Where(one => !run.LastReadings.TryGetValue(one.Key, out var was)
                        || !string.Equals(was, one.Value, StringComparison.Ordinal)
                        || !Equals(run.LastTags.GetValueOrDefault(one.Key), tags.GetValueOrDefault(one.Key)))
                    .ToDictionary(one => one.Key, one => one.Value, StringComparer.OrdinalIgnoreCase);

            if (changed.Count > 0)
            {
                await journal.WriteAsync(
                    run.LastReadings is null ? "reading" : "reading.changed",
                    team.Lead,
                    new
                    {
                        readings = changed.Select(one => new
                        {
                            criterion = one.Key,
                            reading = one.Value,
                            needs = CriterionTags.Spell(tags[one.Key].Needs),
                            tagged = tags[one.Key].By,
                            handover = tags[one.Key].Handover,
                        }),
                    },
                    ct).ConfigureAwait(false);
            }

            run.LastReadings = new Dictionary<string, string>(readings, StringComparer.OrdinalIgnoreCase);
        }

        run.LastTags = tags;

        // A lead that has requests is mid-round whatever status it
        // wrote: blocked on its own requests is how a round is asked
        // for, and a done that still asks for work is not finished.
        // A lead once said done while briefing its first worker, and
        // the run ended with the request never sent. Blocked with
        // nothing requested is blocked; failed is failed either way.
        var hasRequests = report.Requests is { Count: > 0 };

        /*
            Nothing left the run can do: hand it over.

            Every criterion still open needs a push, CI or somebody's
            own check, and the lead asked for no more work. Whatever
            status it wrote, another round cannot get closer - run
            20261023-1740-2677 stopped blocked here, and was picked up
            twice to find nothing to do. Before the gate reminder,
            because that would spend a round too.

            Only on a report that accounts for the criteria, and asks
            for no more work. A criterion that needs CI may still need
            its test written first, and a lead that has said nothing
            about where any of them stand has not got that far.
        */
        if (!hasRequests
            && report.Coverage is { Count: > 0 }
            && held is { Count: > 0 }
            && RunHandovers.Decide(Open(run, held, report), tags) is { Count: > 0 } handed)
        {
            // Before the handover is written, so it names the one branch the
            // person is to push where the node branches could be combined.
            var branches = await _integrate.RunAsync(run, journal, ct).ConfigureAwait(false);

            await journal.WriteAsync(
                "run.handover",
                team.Lead,
                new
                {
                    items = handed.Select(one => new
                    {
                        criterion = one.Criterion,
                        needs = CriterionTags.Spell(one.Needs),
                        todo = one.ToDo,
                        tagged = one.By,
                    }),
                    branches,
                },
                ct).ConfigureAwait(false);

            console.Note(
                $"Nothing left is something this run can do. Handing {handed.Count} criterion(s) to you: "
                + string.Join("; ", handed.Select(one => $"{one.Criterion} ({CriterionTags.Spell(one.Needs)}): {one.ToDo}")));

            run.Ended = RunHandovers.Ended(handed);

            return new LeadTurn(run.Ended, false, null);
        }

        var finishing = report.Status == ReportStatus.Done && !hasRequests;

        // A lead that says done while the team's gate has not been
        // consulted is told once, because its role says to route
        // every change through those nodes and a model that skipped
        // them will otherwise end the run with the work stranded on
        // a branch nobody looked at. Once: the second answer is the
        // lead's to give.
        if (finishing && !run.AskedAboutTheGate
            && MergeStep.Ungated(team, run.Branches, run.Decisions) is { Count: > 0 } pending)
        {
            run.AskedAboutTheGate = true;

            await journal.WriteAsync("gate.reminded", team.Lead, new { pending }, ct).ConfigureAwait(false);

            console.Note($"The lead reported done without the merge gate: {string.Join("; ", pending)}. Telling it once.");

            run.Prompt =
                $"You reported done. This team will not take {string.Join(" or ", run.Branches.Values)} back into "
                + $"the repository until {string.Join(" and ", team.Rules.Gates.Merge)} have accepted it, and "
                + $"{string.Join("; ", pending)}. Request them, or report done again and the work stays on its branch.";

            return new LeadTurn(null, true, null);
        }

        if (finishing
            || report.Status == ReportStatus.Failed
            || (report.Status == ReportStatus.Blocked && !hasRequests))
        {
            /*
                A done the lead could not account for is not a done.

                ReportCheck already refused this twice - a returned
                report goes back once and the second answer is the
                node's, whatever it says. That contract is right for a
                worker and leaves a hole at the top of a run: a lead
                that never fills in coverage is asked twice, and the
                run then ends recording "done" while the journal it
                wrote says two of three criteria were met.

                So the run does not argue further and does not spend
                another round. It records what happened. An autonomous
                run nobody watched must not be readable afterwards as
                having met a goal it did not, and "ended: done" beside
                "2 of 3 met" is exactly that.
            */
            if (report.Status == ReportStatus.Done
                && held is not null
                && Open(run, held, report) is { Count: > 0 } missed)
            {
                await journal.WriteAsync(
                    "goal.unmet",
                    team.Lead,
                    new { criteria = missed },
                    ct).ConfigureAwait(false);

                console.Note(
                    $"The lead reported done without accounting for {missed.Count} of "
                    + $"{held.Count} criteria: {string.Join("; ", missed)}");

                run.Ended = missed.Count == held.Count
                    ? "the lead reported done without accounting for the goal"
                    : $"the lead reported done with {missed.Count} of {held.Count} "
                      + "criteria unmet";

                return new LeadTurn(run.Ended, false, null);
            }

            run.Ended = TeamRunner.Spell(report.Status);
            return new LeadTurn(run.Ended, false, null);
        }

        return new LeadTurn(null, false, report);
    }

    /// <summary>
    /// The criteria the lead has not reported met, leaving out any a person
    /// has checked by hand: those are met on the person's word.
    /// </summary>
    private static List<string> Open(RunContext run, IReadOnlyList<string> held, Report report) =>
        [.. TeamRunner.Outstanding(held, report).Where(one => !run.CheckedByHand.Contains(one.Trim()))];
}

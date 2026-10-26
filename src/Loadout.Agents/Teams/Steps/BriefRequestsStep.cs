using System.Text;
using Loadout.Core.Teams;
using Loadout.Models.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// What acting on a lead's report came to: the run's ending, or the nodes
/// briefed and what the lead will be told about them.
/// </summary>
internal sealed record Briefing(
    string? Ended,
    StringBuilder Feedback,
    IReadOnlyList<ReportRequest> Requests,
    bool HeldForReadings,
    List<string> Refused,
    List<Briefed> Accepted);

/// <summary>
/// Acts on a lead's report: the gate before acting, its questions, how it
/// reads the criteria, and a brief for each node it asked for that it may
/// have.
/// </summary>
internal sealed class BriefRequestsStep
{
    private readonly DecideStep _decide;

    public BriefRequestsStep(DecideStep decide)
    {
        _decide = decide;
    }

    public async Task<Briefing> RunAsync(RunContext run, JournalWriter journal, Report report, CancellationToken ct)
    {
        var (request, team, console, autonomy, runId, directory) =
            (run.Request, run.Team, run.Console, run.Autonomy, run.RunId, run.Directory);
        var (leadNode, teamDirectory, standing) = (run.LeadNode, run.TeamDirectory, run.Standing);

        if (!await TeamRunner.GateAsync(autonomy, console, $"Act on the lead's report (round {run.Rounds}, {TeamRunner.Spell(report.Status)})", ct).ConfigureAwait(false))
        {
            run.Ended = "stopped by the person";
            return Stopped(run);
        }

        var feedback = new StringBuilder();

        if (report.Status == ReportStatus.NeedsDecision)
        {
            var decided = await _decide.RunAsync(run, journal, report.Questions ?? [], ct)
                .ConfigureAwait(false);

            if (decided is null)
            {
                run.Ended = "stopped at a decision";
                return Stopped(run);
            }

            feedback.AppendLine("## Decisions").AppendLine();

            foreach (var (question, answer, by) in decided)
            {
                feedback.AppendLine(TeamRunner.Decided(question, answer, by));
            }

            feedback.AppendLine();
        }

        IReadOnlyList<ReportRequest> requests = report.Requests ?? [];

        /*
          Before the first worker starts, how the lead reads each
          criterion goes to the person.

          A criterion is a sentence somebody wrote in a hurry, and "the
          tests pass" read as "the new test passes" is a misreading that
          costs nothing to correct now and a whole round of workers to
          correct later. Put as an ordinary question with a
          recommendation, so a run with a timer takes it on its own the
          way it takes any other: holding it for a person would mean a
          timed run was almost never unattended. Only where somebody
          could answer - an autonomous run, or one with nobody at a
          terminal or a dashboard, writes the readings down and carries
          on, rather than stopping where it never used to.
        */
        var heldForReadings = false;

        if (!run.ReadingsAccepted && run.Asking && requests.Count > 0 && run.LastReadings is { Count: > 0 })
        {
            var decided = await _decide.RunAsync(run, journal, [TeamRunner.ReadingsQuestion(run.LastReadings, run.LastTags)], GateKind.Readings, ct)
                .ConfigureAwait(false);

            if (decided is null)
            {
                run.Ended = "stopped at a decision";
                return Stopped(run);
            }

            if (decided[0].Answer == TeamRunner.ThinkAgainTold)
            {
                // Nobody briefed: the work it asked for was planned on
                // readings the person has just said are wrong.
                heldForReadings = true;
                requests = [];

                feedback.AppendLine("## Your readings").AppendLine().AppendLine(TeamRunner.ReadingsSentBack).AppendLine();
            }
            else
            {
                run.ReadingsAccepted = true;

                await journal.WriteAsync(
                    "readings.accepted", team.Lead, new { by = decided[0].By }, ct).ConfigureAwait(false);
            }
        }
        else if (!run.ReadingsAccepted && requests.Count > 0 && run.LastReadings is { Count: > 0 })
        {
            // Nobody to ask, so they stand. Said, so the record does
            // not read as though somebody agreed them.
            run.ReadingsAccepted = true;

            await journal.WriteAsync("readings.accepted", team.Lead, new { by = "nobody" }, ct)
                .ConfigureAwait(false);
        }

        var refused = new List<string>();

        // Briefed one at a time, run together, read back in the order
        // the lead asked. Only the middle of those three is worth
        // overlapping: a person cannot answer two gates at once, and
        // a record that arrived in whatever order the nodes finished
        // would read differently every time the same run happened.
        var accepted = new List<Briefed>();

        foreach (var ask in requests)
        {
            // "implementer/1" asks for an instance of the node called
            // "implementer"; the instance name is what the worker is
            // briefed as and reports as.
            var baseName = TeamRunner.BaseNode(ask.Node);

            if (!leadNode.Delegates.Contains(baseName, StringComparer.Ordinal) || !team.Nodes.TryGetValue(baseName, out var node))
            {
                var reason =
                    $"The lead asked for '{ask.Node}', which it may not request. It may request: "
                    + string.Join(", ", leadNode.Delegates) + ".";
                run.Warnings.Add(reason);
                refused.Add(reason);
                await journal.WriteAsync("request.refused", team.Lead, new { node = ask.Node, reason }, ct).ConfigureAwait(false);
                continue;
            }

            // Handed to git, so it has to be a name and nothing else:
            // one starting with a dash would be read as an option. To
            // a node with a tree of its own it is where the tree
            // starts; to one without, the work it reads, which is how
            // the first lead to have it used it on a reviewer.
            if (ask.From is { } from && !TeamRunner.IsRef(from))
            {
                var reason = $"The lead asked for '{ask.Node}' from '{from}', which was refused: '{from}' is not the name of a commit or branch.";
                run.Warnings.Add(reason);
                refused.Add(reason);
                await journal.WriteAsync("request.refused", team.Lead, new { node = ask.Node, reason }, ct).ConfigureAwait(false);
                continue;
            }

            // The one checkpoint that offers a third answer. Yes and no
            // make somebody choose between the wrong brief and no brief,
            // and the lead being wrong about a task is both ordinary and
            // expensive: the worker goes off and does the wrong thing,
            // competently, for ten minutes.
            var task = ask.Task;

            if (autonomy == "manual")
            {
                var revised = await console
                    .ReviseAsync($"Brief {ask.Node} ({node.Role})", task, GateKind.Brief, ct)
                    .ConfigureAwait(false);

                if (revised is null)
                {
                    run.Ended = "stopped by the person";
                    return Stopped(run);
                }

                if (!string.Equals(revised, task, StringComparison.Ordinal))
                {
                    await journal.WriteAsync(
                        "brief.revised",
                        ask.Node,
                        new { was = task, now = revised },
                        ct).ConfigureAwait(false);

                    task = revised;
                }
            }
            else
            {
                console.Note($"Brief {ask.Node} ({node.Role}): {task}");
            }

            var role = request.Specialists.Find(node.Role)!;
            var brief = TeamRunner.MakeBrief(
                runId, ask.Node, team.Lead, node, role, task, ask.Inputs ?? [], doneWhen: [], team, autonomy,
                request.OutwardAllowed ?? [], request.Specialists.Find, teamDirectory,
                TeamRunner.Held(standing, run.Criteria), ask.From);

            await TeamRunner.WriteDocumentAsync(directory, $"brief-{TeamRunner.Safe(ask.Node)}-{run.Rounds}.json", ReportReader.Write(brief), ct).ConfigureAwait(false);

            if (brief.Constraints.Worktree is { Length: > 0 } made)
            {
                run.Branches[ask.Node] = made;
            }

            accepted.Add(new Briefed(ask, node, role, brief));
        }

        return new Briefing(null, feedback, requests, heldForReadings, refused, accepted);
    }

    // Where the run ends part way through acting on the report: what was
    // briefed by then is never run and nothing is said to the lead again, so
    // only the ending is handed back.
    private static Briefing Stopped(RunContext run) =>
        new(run.Ended, new StringBuilder(), [], false, [], []);
}

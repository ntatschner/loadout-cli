using Loadout.Core.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// What is checked before every lead turn: the round limit, the budget, a
/// stop, a hold, and anything said to the lead in the meantime.
/// </summary>
internal sealed class RoundLimitsStep
{
    private readonly TimeProvider _time;

    public RoundLimitsStep(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>
    /// Why the run ends here, or null when the next round may start - in which
    /// case it has been counted and written down.
    /// </summary>
    public async Task<string?> RunAsync(RunContext run, JournalWriter journal, CancellationToken ct)
    {
        var (request, team, console, directory) = (run.Request, run.Team, run.Console, run.Directory);

        // Checked here rather than at the end of the loop, because the
        // end is not the only way back to the top. The merge-gate
        // reminder continues past it, which bought a fourth round on a
        // run that asked for three - a real lead turn, real money, and
        // a summary that said "round limit: 3 rounds ... 4 rounds" in
        // one breath. Found by the first run that reached the reminder.
        // Only where one was asked for. A round is a crude ceiling on
        // a thing measured in money: it stopped runs that were working
        // and let expensive ones through, and the run that prompted
        // this used four of its five rounds while spending 20.84 of a
        // 25 budget - the cap doing the work was the money and the cap
        // in the way was the rounds. Nothing here is uncapped: the
        // check above refuses a run that has neither.
        if (request.MaxRounds > 0 && run.Rounds >= request.MaxRounds)
        {
            run.Ended = $"round limit: {request.MaxRounds} round{(request.MaxRounds == 1 ? string.Empty : "s")}";
            return run.Ended;
        }

        // And the money, here for exactly the reason above. It was at
        // the end of the loop, where the reminder's way back to the
        // top does not pass, so a lead that reported done over budget
        // was told about the gate and given another turn to answer -
        // spending past a ceiling the run had already reached. One
        // turn, once, which is small; it is also the only check
        // standing between an unattended run and its budget, and a
        // ceiling with a way round it is not one.
        //
        // The team's figure unless somebody changed it while the run
        // was going, which is read here each round so a raise from the
        // dashboard or `team budget` lands before the next lead turn.
        var ceiling = RunControl.Cap(directory).Or(TeamRunner.StartingCap(request, team));

        if (ceiling != run.HeldTo)
        {
            run.HeldTo = ceiling;

            await journal.WriteAsync(
                "run.budget", null, new { budget = ceiling.Usd, uncapped = ceiling.None, by = "you" }, ct)
                .ConfigureAwait(false);
        }

        if (ceiling.Usd is { } spendable && run.Cost >= spendable)
        {
            // Somebody is watching, so the run asks rather than ends:
            // a run stopped on its budget with the goal half done was
            // otherwise a run that could not be picked up again.
            if (run.Asking && await TeamRunner.RaiseAsync(console, run.Cost, spendable, ct).ConfigureAwait(false) is { IsSet: true } raised)
            {
                await RunControl.SetCapAsync(directory, raised, ct).ConfigureAwait(false);

                run.HeldTo = raised;

                await journal.WriteAsync(
                    "run.budget", null, new { budget = raised.Usd, uncapped = raised.None, by = "you" }, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                run.Ended = $"budget spent: {run.Cost:0.00} of {spendable:0.00} USD";
                return run.Ended;
            }
        }

        // Asked to stop. Between rounds rather than mid-turn, because
        // a node is a headless agent in the middle of one and the only
        // ways to end that sooner are to kill it - losing the turn and
        // what was paid for it - or to ask it, which it cannot hear.
        if (RunControl.Stopped(directory))
        {
            run.Ended = "stopped by request";
            return run.Ended;
        }

        // Or asked to hold. Nothing is spent while it waits, and it
        // carries on the moment the hold is lifted.
        if (RunControl.Paused(directory))
        {
            await journal.WriteAsync("run.paused", null, new { round = run.Rounds + 1 }, ct)
                .ConfigureAwait(false);

            console.Note("Held. It carries on when you let it.");

            while (RunControl.Paused(directory) && !ct.IsCancellationRequested)
            {
                await Task.Delay(NodePermissions.Glance, _time, ct).ConfigureAwait(false);
            }

            if (RunControl.Stopped(directory))
            {
                run.Ended = "stopped by request";
                return run.Ended;
            }

            await journal.WriteAsync("run.resumed", null, new { round = run.Rounds + 1 }, ct)
                .ConfigureAwait(false);
        }

        // Anything said to the lead while it was working, handed over
        // before it takes its next turn, in the order it was said.
        if (RunControl.TakeMessages(directory) is { Count: > 0 } messages)
        {
            foreach (var message in messages)
            {
                await journal.WriteAsync("run.told", team.Lead, new { message }, ct)
                    .ConfigureAwait(false);
            }

            run.Prompt = "The person running this team says:\n\n"
                + string.Join("\n\n", messages)
                + "\n\n"
                + run.Prompt;
        }

        run.Rounds++;

        // Written as it starts rather than counted at the end, so a
        // run still going can be told how far through it is.
        await journal.WriteAsync("round.started", null, new { round = run.Rounds, of = request.MaxRounds }, ct)
            .ConfigureAwait(false);

        return null;
    }
}

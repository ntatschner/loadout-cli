using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Results;

namespace Loadout.Agents.Ideas;

/// <summary>What one round with a detached agent came to.</summary>
/// <param name="Answer">What was read from its answer, or why nothing could be.</param>
/// <param name="Plan">What was started, or would have been.</param>
/// <param name="Warnings">Anything worth saying about how it was started.</param>
/// <param name="CostUsd">What it cost, when the agent said.</param>
/// <param name="Started">False for a dry run, where nothing was asked.</param>
internal sealed record SchemaRoundOutcome<T>(
    OperationResult<T> Answer,
    LaunchPlan Plan,
    IReadOnlyList<string> Warnings,
    decimal CostUsd,
    bool Started);

/// <summary>
/// One question put to a fresh agent that must answer in a given shape: start
/// it, ask, read, ask once more if the shape was wrong, end it.
/// </summary>
/// <remarks>
/// Shared by refining an idea and splitting a dump, which differ in what they
/// ask and how they read the answer and in nothing else. The agent reads at
/// most; what it may use is named, nothing asks, and neither hooks nor any MCP
/// server of the machine's are brought along.
/// </remarks>
internal static class SchemaRound
{
    /// <summary>
    /// How long a round may take before it is given up on. A session has no
    /// watchdog of its own, and a round nobody is watching that never ends is
    /// a command that never returns.
    /// </summary>
    internal static readonly TimeSpan Limit = TimeSpan.FromMinutes(20);

    /// <summary>Named as well as left out, so a looser posture elsewhere cannot let them back in.</summary>
    internal static readonly IReadOnlyList<string> Denied =
        ["Write", "Edit", "NotebookEdit", "Bash", "WebFetch", "WebSearch"];

    private static readonly TimeSpan EndGrace = TimeSpan.FromSeconds(10);

    public static async Task<OperationResult<SchemaRoundOutcome<T>>> AskAsync<T>(
        IDetachedLauncher launcher,
        DetachedLaunchRequest request,
        IReadOnlyList<string> allowed,
        string schema,
        string contract,
        string prompt,
        Func<string?, OperationResult<T>> read,
        Func<string, Task>? watching,
        CancellationToken ct)
    {
        var options = new HeadlessOptions(
            Permission: HeadlessPermission.DenyUnlessAllowed,
            AllowedTools: allowed,
            DeniedTools: Denied,
            MaxTurns: 40,
            OutputSchemaJson: schema,
            DisableHooks: true,
            IsolateMcpServers: true);

        var started = await launcher.StartAsync(request, options, ct).ConfigureAwait(false);

        if (started.Failed)
        {
            return OperationResult<SchemaRoundOutcome<T>>.Fail(started.Error!, started.ExitCode);
        }

        await using var launch = started.Value!;

        if (launch.Session is not { } session)
        {
            return OperationResult<SchemaRoundOutcome<T>>.Ok(new SchemaRoundOutcome<T>(
                OperationResult<T>.Fail("Nothing was asked."), launch.Plan, launch.Warnings, 0, Started: false));
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Limit);

        var cost = 0m;
        OperationResult<T> answer;

        try
        {
            var turn = await session.TurnAsync(prompt, Watch(watching), limit.Token).ConfigureAwait(false);

            cost += turn.CostUsd;
            answer = Answer(turn, session, read);

            // Asked once more when the answer came back in the wrong shape.
            // The agent usually has the substance and slipped on the form, and
            // one more turn is cheaper than the round it would otherwise take
            // to start again. Not after an error: the agent has said it could
            // not go on, and asking again would bury what it said under
            // whatever the second turn made of being asked.
            if (answer.Failed && turn.Completed && !turn.Result!.IsError)
            {
                var again = await session.TurnAsync(
                    $"That answer was not usable: {answer.Error} Give your answer again, in the "
                    + $"{contract} shape and nothing else.",
                    Watch(watching),
                    limit.Token).ConfigureAwait(false);

                cost += again.CostUsd;
                answer = Answer(again, session, read);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            answer = OperationResult<T>.Fail(
                $"The agent had not answered after {Limit.TotalMinutes:0} minutes, so the round was stopped.");
        }

        var (exit, _) = await session.EndAsync(EndGrace, CancellationToken.None).ConfigureAwait(false);
        await launch.CompleteAsync(exit, CancellationToken.None).ConfigureAwait(false);

        return OperationResult<SchemaRoundOutcome<T>>.Ok(
            new SchemaRoundOutcome<T>(answer, launch.Plan, launch.Warnings, cost, Started: true));
    }

    private static OperationResult<T> Answer<T>(
        HeadlessTurn turn,
        HeadlessSession session,
        Func<string?, OperationResult<T>> read)
    {
        if (!turn.Completed)
        {
            var said = session.StandardError.TakeLast(5).ToList();

            return OperationResult<T>.Fail(
                "The agent stopped before it answered."
                + (said.Count > 0 ? $" It said: {string.Join(" ", said)}" : string.Empty));
        }

        return turn.Result!.IsError
            ? OperationResult<T>.Fail($"The agent ended the round with an error ({turn.Result.Subtype}).")
            : read(turn.StructuredOutputJson);
    }

    private static Func<HeadlessEvent, CancellationToken, Task>? Watch(Func<string, Task>? watching) =>
        watching is null
            ? null
            : (seen, _) => seen switch
            {
                HeadlessToolUse { FromSubagent: false } used => watching(used.Tool),
                _ => Task.CompletedTask,
            };
}

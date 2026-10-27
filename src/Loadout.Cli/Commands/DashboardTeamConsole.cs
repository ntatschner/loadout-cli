using Loadout.Agents.Teams;
using Loadout.Core.Teams;
using Loadout.Models.Teams;

namespace Loadout.Cli.Commands;

/// <summary>
/// The person in a browser, as a run sees them.
/// </summary>
/// <remarks>
/// <para>
/// Used where a run has nobody at a terminal but a daemon is serving the
/// dashboard: a scheduled run, one a webhook started, one the daemon fired on
/// a commit. Those are exactly the runs that used to have to decide everything
/// themselves or refuse.
/// </para>
/// <para>
/// A terminal run keeps answering at its terminal. Two places able to answer
/// one question is a race whose loser leaves a dead prompt on somebody's
/// screen, so which one can answer is decided once, by whether there is a
/// terminal, rather than per question.
/// </para>
/// <para>
/// Every question goes out as a file in the run's directory and the answer
/// comes back as one, which is the same exchange a node's permission question
/// already uses. The dashboard is a different process from the run - it is the
/// daemon, and the run is a command the daemon started - so the directory is
/// the only channel there is.
/// </para>
/// </remarks>
public sealed class DashboardTeamConsole : ITeamConsole
{
    private readonly TimeProvider _time;
    private readonly Action<string> _note;
    private readonly TimeSpan _wait;

    private string? _directory;

    /// <param name="time">The clock.</param>
    /// <param name="note">Told what the console is doing, for the run's output.</param>
    /// <param name="wait">
    /// How long a question waits for a person: the machine's
    /// <c>team-question-wait</c>, or an hour where none is given.
    /// </param>
    public DashboardTeamConsole(TimeProvider time, Action<string> note, TimeSpan? wait = null)
    {
        _time = time;
        _note = note;
        _wait = wait ?? NodePermissions.PersonPatience;
    }

    /// <inheritdoc />
    public void Starting(string runDirectory) => _directory = runDirectory;

    /// <summary>
    /// Whether anybody could answer.
    /// </summary>
    /// <remarks>
    /// True once the run has somewhere to write. A question with nowhere to go
    /// is worse than no question: the node waits out its patience and is
    /// refused anyway, later and having done nothing in between.
    /// </remarks>
    public bool CanAsk => _directory is { Length: > 0 };

    /// <summary>
    /// Yes: every question this console asks is a file in the run directory.
    /// </summary>
    /// <remarks>
    /// Which is also where a node's own questions arrive, so the watcher that
    /// carries one up must not hand it to this console - it would be writing
    /// the question back into the queue it just read it from.
    /// </remarks>
    public bool AnswersInPlace => true;

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string what, CancellationToken ct = default) =>
        ConfirmAsync(what, GateKind.Confirm, ct);

    /// <inheritdoc />
    /// <remarks>
    /// The kind goes in the question's file, which is where a page reads it:
    /// a merge is a confirm whose no leaves the branch and carries on.
    /// </remarks>
    public async Task<bool> ConfirmAsync(string what, string kind, CancellationToken ct = default)
    {
        if (_directory is not { Length: > 0 } directory)
        {
            // Nowhere to ask and nobody to ask. Refusing is the safe direction
            // and is what a run with no terminal did before any of this.
            return false;
        }

        _note($"Waiting for somebody at the dashboard: {what}?");

        var answer = await NodePermissions.AskAsync(
            directory,
            new PendingAsk(
                Id: $"gate-{Guid.NewGuid().ToString("N")[..8]}",
                Node: "run",
                Role: "the run",
                Tool: string.Empty,
                Target: null,
                At: _time.GetUtcNow(),
                Kind: kind,
                Asked: what),
            _time,

            // The wait for somebody who has to read this before they can answer
            // it, which is not the wait for a node stopped mid-turn.
            _wait,
            ct).ConfigureAwait(false);

        return answer?.Allowed ?? false;
    }

    /// <summary>
    /// Puts the brief a worker would be given in front of somebody, for
    /// changing before it goes.
    /// </summary>
    /// <remarks>
    /// The lead wrote that task and can be wrong about it in a way that is
    /// obvious to whoever is watching and expensive to find out any other way.
    /// A checkpoint that only says yes or no makes somebody choose between the
    /// wrong brief and no brief.
    /// </remarks>
    public Task<string?> ReviseAsync(string what, string task, CancellationToken ct = default) =>
        ReviseAsync(what, task, GateKind.Brief, ct);

    /// <inheritdoc />
    public async Task<string?> ReviseAsync(string what, string task, string kind, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(task);

        if (_directory is not { Length: > 0 } directory)
        {
            return null;
        }

        _note($"Waiting for somebody at the dashboard: {what}");

        var answer = await NodePermissions.AskAsync(
            directory,
            new PendingAsk(
                Id: $"gate-{Guid.NewGuid().ToString("N")[..8]}",
                Node: "run",
                Role: "the run",
                Tool: string.Empty,
                Target: null,
                At: _time.GetUtcNow(),

                // Its own kind, so a page can offer a box rather than two
                // buttons. Anything that does not know the kind falls back to
                // yes and no, which is the answer it had before. A brief or the
                // criteria the run will be judged on: both are a box, and only
                // one of them is somebody about to be briefed.
                Kind: kind,
                Asked: what,
                Recommendation: task),
            _time,

            // The wait for somebody who has to read this before they can answer
            // it, which is not the wait for a node stopped mid-turn.
            _wait,
            ct).ConfigureAwait(false);

        if (answer is null || !answer.Allowed)
        {
            return null;
        }

        return answer.Instead is { Length: > 0 } instead ? instead : task;
    }

    /// <inheritdoc />
    public Task<string?> DecideAsync(ReportQuestion question, CancellationToken ct = default) =>
        DecideAsync(question, GateKind.Question, null, ct);

    /// <inheritdoc />
    /// <remarks>
    /// The timer goes in the question's file, where the page and the launcher
    /// read it, so both can count down to what will actually happen.
    /// </remarks>
    public Task<string?> DecideAsync(ReportQuestion question, QuestionTimer timer, CancellationToken ct = default) =>
        DecideAsync(question, GateKind.Question, timer, ct);

    /// <inheritdoc />
    public async Task<string?> DecideAsync(
        ReportQuestion question, string kind, QuestionTimer? timer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (_directory is not { Length: > 0 } directory)
        {
            return null;
        }

        _note($"Waiting for somebody at the dashboard: {question.Question}");

        var answer = await NodePermissions.AskAsync(
            directory,
            new PendingAsk(
                Id: $"gate-{Guid.NewGuid().ToString("N")[..8]}",
                Node: "lead",
                Role: "the lead",
                Tool: string.Empty,
                Target: null,
                At: _time.GetUtcNow(),
                Kind: kind,
                Asked: question.Question,

                // The lead's own options, plus the two it cannot offer: sending
                // the question back to it, and stopping. Not sending back the
                // budget, which the lead never asked: "Think again" there was
                // read as a figure that was not one, and ended the run.
                Options: GateKind.ThinksAgain(kind)
                    ? [.. question.Options, TeamRunner.ThinkAgain, Stop]
                    : [.. question.Options, Stop],
                Recommendation: question.Recommendation,
                Timer: timer),
            _time,

            // The wait for somebody who has to read this before they can answer
            // it, which is not the wait for a node stopped mid-turn.
            _wait,
            ct).ConfigureAwait(false);

        // Nobody answered, or somebody chose to stop. Both end the run, and
        // both are a null here, which is what the runner reads as "stop".
        return answer?.Chosen is { Length: > 0 } chosen && chosen != Stop ? chosen : null;
    }

    /// <summary>The option a lead never offers and a person always has.</summary>
    private const string Stop = "Stop the run";

    /// <inheritdoc />
    public void Note(string line) => _note(line);
}

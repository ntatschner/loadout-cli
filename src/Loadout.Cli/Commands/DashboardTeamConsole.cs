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

    private string? _directory;

    public DashboardTeamConsole(TimeProvider time, Action<string> note)
    {
        _time = time;
        _note = note;
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

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string what, CancellationToken ct = default)
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
                Kind: "confirm",
                Asked: what),
            _time,
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
    public async Task<string?> ReviseAsync(string what, string task, CancellationToken ct = default)
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
                // yes and no, which is the answer it had before.
                Kind: "brief",
                Asked: what,
                Recommendation: task),
            _time,
            ct).ConfigureAwait(false);

        if (answer is null || !answer.Allowed)
        {
            return null;
        }

        return answer.Instead is { Length: > 0 } instead ? instead : task;
    }

    /// <inheritdoc />
    public async Task<string?> DecideAsync(ReportQuestion question, CancellationToken ct = default)
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
                Kind: "question",
                Asked: question.Question,

                // The lead's own options, plus the one it cannot offer: stop.
                Options: [.. question.Options, Stop],
                Recommendation: question.Recommendation),
            _time,
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

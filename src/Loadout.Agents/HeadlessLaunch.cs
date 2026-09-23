using Loadout.Core.Diagnostics;

namespace Loadout.Agents;

/// <summary>
/// A node the launcher has started, or would have: the session to talk to
/// and the records to close when it is done.
/// </summary>
/// <remarks>
/// <para>
/// The interactive launch waits for its agent and closes its own records
/// on the way out. A headless one is handed back still running, because
/// the caller is the one with messages to send, so closing the records is
/// the caller's to do, through <see cref="CompleteAsync"/>, once and with
/// the exit code it saw. Disposing without completing ends the session
/// first, then completes with whatever exit code that produced, so a
/// caller that gives up still leaves the ledger, the running list and the
/// runtime directory as they should be.
/// </para>
/// <para>
/// On a dry run there is no session and nothing to complete; the plan is
/// the whole of the answer.
/// </para>
/// </remarks>
public sealed class HeadlessLaunch : IAsyncDisposable
{
    private readonly Func<int?, CancellationToken, Task> _complete;
    private bool _completed;

    public HeadlessLaunch(
        LaunchPlan plan,
        IReadOnlyList<string> warnings,
        PreflightResult preflight,
        string projectName,
        string agentName,
        string? launchId,
        HeadlessSession? session,
        Func<int?, CancellationToken, Task> complete)
    {
        Plan = plan;
        Warnings = warnings;
        Preflight = preflight;
        ProjectName = projectName;
        AgentName = agentName;
        LaunchId = launchId;
        Session = session;
        _complete = complete;
        _completed = session is null;
    }

    /// <summary>What the node was started with, or would have been.</summary>
    public LaunchPlan Plan { get; }

    /// <summary>Everything worth telling the person about how the launch was put together.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>What preflight found.</summary>
    public PreflightResult Preflight { get; }

    /// <summary>The project as a person calls it.</summary>
    public string ProjectName { get; }

    /// <summary>The adapter that ran.</summary>
    public string AgentName { get; }

    /// <summary>The ledger's identifier for this launch, or null on a dry run.</summary>
    public string? LaunchId { get; }

    /// <summary>The conversation with the node, or null on a dry run.</summary>
    public HeadlessSession? Session { get; }

    /// <summary>
    /// Closes the launch's records with the exit code the caller saw, or
    /// null for a node that never ran to an exit. Safe to call once; later
    /// calls do nothing.
    /// </summary>
    public async Task CompleteAsync(int? exitCode, CancellationToken ct = default)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        await _complete(exitCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Session is null)
        {
            return;
        }

        int? exitCode = null;

        if (!_completed)
        {
            // A caller that is disposing without having completed has given
            // up on the conversation. The agent is told, given a moment, and
            // killed if it stays; the ledger gets the code that produced.
            var (code, _) = await Session.EndAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            exitCode = code;
        }

        await Session.DisposeAsync().ConfigureAwait(false);
        await CompleteAsync(exitCode).ConfigureAwait(false);
    }
}

using System.Text.Json;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Folds what a node left in the run's directory - the permission questions
/// its answerer decided, and its own account of what it was doing - into the
/// journal, once the node has gone.
/// </summary>
internal sealed class FoldStep
{
    private readonly IPlatformPaths _paths;

    public FoldStep(IPlatformPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Takes what the answerer recorded into the run's own record, once the
    /// node that asked has gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answerer runs in another process, so it writes its own file rather
    /// than appending to the journal: two processes appending to one file is
    /// how a record acquires half lines. This is the fold, and it happens
    /// after the node has ended, when nothing more can be written to it.
    /// </para>
    /// <para>
    /// A refusal is worth telling the person about, because it is usually not
    /// a node misbehaving. It is a role whose lists are narrower than the work
    /// it was given, and nobody finds that out from a count of denials.
    /// </para>
    /// <para>
    /// The run context is taken so that every step is called the same way;
    /// the warnings are passed separately because a worker folds into its own
    /// list, not the run's.
    /// </para>
    /// </remarks>
    public async Task RunAsync(
        RunContext run,
        JournalWriter journal,
        Brief brief,
        List<string> warnings,
        CancellationToken ct)
    {
        var path = Path.Combine(RunDirectory(brief.Run), NodePermissions.AskedFileName(brief.Node));

        string[] lines;

        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);

            // Removed once folded, so a node briefed a second time in the
            // same run does not have its first turn's questions counted
            // again.
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var refused = new List<string>();

        foreach (var line in lines)
        {
            if (RunJournal.Parse($$"""{"kind":"permission.asked","data":{{line}}}""") is not { } asked)
            {
                continue;
            }

            // The decision carries its own time. Dating it at the fold is what
            // put it after the node's death in the log.
            await journal
                .WriteAsync("permission.asked", brief.Node, asked.Data, ct, When(asked))
                .ConfigureAwait(false);

            if (asked.Data.TryGetProperty("allowed", out var allowed) && allowed.ValueKind == JsonValueKind.False)
            {
                var what = asked.Text("tool") ?? "something";
                var target = asked.Text("target");

                refused.Add(target is { Length: > 0 } ? $"{what} ({target})" : what);
            }
        }

        if (refused.Count > 0)
        {
            warnings.Add(
                $"{brief.Node} asked for {refused.Count} thing(s) its role does not allow: "
                + string.Join(", ", refused.Distinct(StringComparer.Ordinal))
                + ". Either the role is narrower than the work, or the node was going somewhere it should not.");
        }

        await FoldProgressAsync(brief, journal, ct).ConfigureAwait(false);
    }

    /// <summary>When a folded event says it happened, or nothing.</summary>
    private static DateTimeOffset? When(RunEvent asked) =>
        asked.Word("at") is { } said
        && DateTimeOffset.TryParse(
            said,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var then)
            ? then
            : null;

    /// <summary>
    /// Folds a node's own account of what it was doing into the journal.
    /// </summary>
    /// <remarks>
    /// Kept as its own kind of entry rather than merged with what the run
    /// observed. Two accounts, and neither corrects the other: the run's is
    /// precise about what happened and says nothing about why, the node's says
    /// why and may be wrong. Merging them would lose whichever disagreed.
    /// </remarks>
    private async Task FoldProgressAsync(Brief brief, JournalWriter journal, CancellationToken ct)
    {
        var path = Path.Combine(RunDirectory(brief.Run), NodeProgress.FileName(brief.Node));

        var said = NodeProgress.Read(path);

        if (said.Count == 0)
        {
            return;
        }

        foreach (var one in said)
        {
            await journal
                .WriteAsync("node.said", brief.Node, new { step = one.Step, of = one.Of, doing = one.Doing, line = one.Line }, ct)
                .ConfigureAwait(false);
        }

        try
        {
            // Removed once folded, so a node briefed twice in one run does not
            // have its first turn's account replayed into the second.
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // The same place TeamRunner keeps a run, worked out the same way.
    private string RunDirectory(string runId) =>
        Path.Combine(_paths.Paths.State, "teams", "runs", runId);
}

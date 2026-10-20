using System.Text.Json;
using Loadout.Core.Configuration;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Models.Results;

namespace Loadout.Core.Ideas;

/// <summary>
/// One change to an idea's record: why it is refused before anything is read,
/// the words to screen for a credential, and the change itself, which says
/// why it cannot be made or makes it.
/// </summary>
/// <param name="Refusal">Why it is refused whatever the record says, or null.</param>
/// <param name="Screen">Text it would write, screened before anything is read; null for none.</param>
/// <param name="Change">Makes the change to the record in memory, or says why it cannot, changing nothing.</param>
internal sealed record IdeaEdit(string? Refusal, string? Screen, Func<IdeaRecord, string?> Change);

/// <summary>What one edit of an idea's record came to.</summary>
/// <param name="Result">The record as it now stands, or why not.</param>
/// <param name="Failed">Which edit was refused, or null when none was.</param>
/// <param name="Revision">The record's revision now, or as it was when nothing was written; null when it could not be read.</param>
internal sealed record IdeaEdited(OperationResult<IdeaRecord> Result, int? Failed, string? Revision);

internal sealed partial class IdeaService
{
    /// <inheritdoc />
    public async Task<OperationResult<IdeaRead>> ReadWithRevisionAsync(IdeaPlace place, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (Unavailable() is { } missing)
        {
            return OperationResult<IdeaRead>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        var path = RecordPath(place);

        if (!File.Exists(path))
        {
            return OperationResult<IdeaRead>.Fail(NotFound(place), ExitCode.InvalidArguments);
        }

        var read = await _kept.ReadAsync(path, ct).ConfigureAwait(false);

        return read.Succeeded
            ? OperationResult<IdeaRead>.Ok(new IdeaRead(place, read.Value!.Value, read.Value.Revision))
            : OperationResult<IdeaRead>.Fail(read.Error!, read.ExitCode);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdeaStamp>> StampsAsync(CancellationToken ct = default)
    {
        if (Unavailable() is not null)
        {
            return [];
        }

        var stamps = new List<IdeaStamp>();

        foreach (var list in Lists())
        {
            var directory = TaskPaths.Ideas(_workspace.LocalPath, list);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.yaml").Order(StringComparer.Ordinal))
            {
                var place = new IdeaPlace(list, Path.GetFileNameWithoutExtension(path));
                var stamp = FileStamp.Of(path).Text;

                // With the revision as well, because two writes inside one
                // tick of a coarse clock, with the same length, have the same
                // time and length. Always, rather than only while the file is
                // settling, or the stamp would change again when it settled
                // and say twice that one write happened. A settled file's
                // revision comes from what is kept, without reading it.
                if (await _kept.ReadAsync(path, ct).ConfigureAwait(false) is { Succeeded: true, Value: { } read })
                {
                    stamp += "#" + read.Revision;
                }

                // A round starting or ending changes what the idea shows
                // before it changes the record, so whether one is running is
                // part of the stamp.
                if (_locks.Held(place))
                {
                    stamp += "+running";
                }

                stamps.Add(new IdeaStamp(place, stamp));
            }
        }

        return stamps;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Applied in order. The answers, choices, verdicts and dismissals for one
    /// idea are made together at the place the first of them comes, all or
    /// nothing, under one lock and with one write, because one record is one
    /// file and one file is what its revision guards. A capture and a remove
    /// change the list as well as the record, and stand on their own at their
    /// own place in the batch.
    /// </para>
    /// <para>
    /// A remove that names a revision is checked against it before the
    /// removal, not under the same lock, because the removal goes through the
    /// task list's own lock: a change landing between the two is removed with
    /// the rest, which for a removal is what was asked.
    /// </para>
    /// </remarks>
    public async Task<OperationResult<IdeaBatch>> ApplyAsync(IReadOnlyList<IdeaOperation> operations, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        // An empty name is the workspace-wide list, made null so the two are one list.
        operations = [.. operations.Select(one => one with { Project = one.Project is { Length: > 0 } ? one.Project : null })];

        var outcomes = new IdeaOutcome?[operations.Count];

        for (var index = 0; index < operations.Count; index++)
        {
            if (outcomes[index] is not null)
            {
                continue;
            }

            var operation = operations[index];

            if (TaskPaths.Rejection(operation.Project) is { } project)
            {
                outcomes[index] = Failed(index, operation, project, ExitCode.InvalidArguments);

                continue;
            }

            if (operation.Op != IdeaOperation.Capture && TaskIds.Rejection(operation.Id) is { } id)
            {
                outcomes[index] = Failed(index, operation, id, ExitCode.InvalidArguments);

                continue;
            }

            switch (operation.Op)
            {
                case IdeaOperation.Capture:
                {
                    var captured = await CaptureAsync(
                        operation.Project, operation.Text ?? string.Empty, operation.By, operation.Id, ct, operation.Title).ConfigureAwait(false);

                    outcomes[index] = await AfterAsync(index, operation, captured, captured.Value?.Id, ct).ConfigureAwait(false);

                    break;
                }

                case IdeaOperation.Remove:
                {
                    var place = new IdeaPlace(operation.Project, operation.Id!.Trim());

                    if (operation.Revision is { } expected
                        && await ReadWithRevisionAsync(place, ct).ConfigureAwait(false) is { Succeeded: true, Value: { } current }
                        && current.Revision != expected)
                    {
                        outcomes[index] = Failed(index, operation, Stale(place, expected, current.Revision), ExitCode.ChangedSinceRead);

                        break;
                    }

                    var removed = await RemoveAsync(place, ct).ConfigureAwait(false);

                    outcomes[index] = removed.Succeeded
                        ? new IdeaOutcome(index, operation.Op, operation.Project, place.Id, null, ExitCode.Success, null, null)
                        : Failed(index, operation, removed.Error!, removed.ExitCode);

                    break;
                }

                case var edit when IdeaOperation.Edits.Contains(edit):
                {
                    var place = new IdeaPlace(operation.Project, operation.Id!.Trim());

                    // Every edit of this idea in the batch, made here together.
                    var group = Enumerable.Range(index, operations.Count - index)
                        .Where(one => outcomes[one] is null
                            && IdeaOperation.Edits.Contains(operations[one].Op)
                            && string.Equals(operations[one].Project, place.Project, StringComparison.Ordinal)
                            && operations[one].Id is { } named && Same(named.Trim(), place.Id))
                        .ToList();

                    var revision = group.Select(one => operations[one].Revision).FirstOrDefault(one => one is not null);
                    var edited = await EditedAsync(place, [.. group.Select(one => Edit(place, operations[one]))], revision, ct)
                        .ConfigureAwait(false);

                    for (var at = 0; at < group.Count; at++)
                    {
                        var one = group[at];

                        outcomes[one] = edited.Result.Succeeded
                            ? new IdeaOutcome(one, operations[one].Op, place.Project, place.Id, null, ExitCode.Success, edited.Result.Value, edited.Revision)
                            : edited.Failed is { } failed && failed != at
                                ? Failed(
                                    one,
                                    operations[one],
                                    $"Not applied, because operation {group[failed]} could not be ({edited.Result.Error}) and "
                                    + $"{place.Id} is changed all or nothing.",
                                    edited.Result.ExitCode) with { HeldBack = true }
                                : Failed(one, operations[one], edited.Result.Error!, edited.Result.ExitCode);
                    }

                    break;
                }

                default:
                    outcomes[index] = Failed(
                        index,
                        operation,
                        $"There is nothing called '{operation.Op}' to do to an idea. It is one of {string.Join(", ", IdeaOperation.Ops)}.",
                        ExitCode.InvalidArguments);

                    break;
            }
        }

        return OperationResult<IdeaBatch>.Ok(new IdeaBatch([.. outcomes.Select(one => one!)]));
    }

    /// <summary>One operation's change, as the single-entry method makes it.</summary>
    private static IdeaEdit Edit(IdeaPlace place, IdeaOperation operation) => operation.Op switch
    {
        IdeaOperation.Answering => Answer(place, operation.Question ?? string.Empty, operation.Answer ?? string.Empty),
        IdeaOperation.Choose => Choose(operation.Layer ?? string.Empty, operation.Option ?? string.Empty),
        IdeaOperation.Judge => Judge(operation.Piece ?? string.Empty, operation.Verdict ?? IdeaVerdict.Undecided, operation.Request),
        _ => Dismiss(place),
    };

    /// <summary>What a capture came to, with the record's revision as it now stands.</summary>
    private async Task<IdeaOutcome> AfterAsync(
        int index, IdeaOperation operation, OperationResult<IdeaRecord> done, string? id, CancellationToken ct)
    {
        if (done.Failed)
        {
            return Failed(index, operation, done.Error!, done.ExitCode);
        }

        var read = await ReadWithRevisionAsync(new IdeaPlace(operation.Project, id!), ct).ConfigureAwait(false);

        return new IdeaOutcome(index, operation.Op, operation.Project, id!, null, ExitCode.Success, done.Value, read.Value?.Revision);
    }

    /// <summary>
    /// Makes several changes to one idea's record together, or none of them,
    /// under one lock and with one write.
    /// </summary>
    private async Task<OperationResult<IdeaRecord>> EditAsync(
        IdeaPlace place,
        IReadOnlyList<IdeaEdit> edits,
        string? revision,
        CancellationToken ct) =>
        (await EditedAsync(place, edits, revision, ct).ConfigureAwait(false)).Result;

    private async Task<IdeaEdited> EditedAsync(
        IdeaPlace place,
        IReadOnlyList<IdeaEdit> edits,
        string? revision,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(place);

        IdeaEdited Refused(int? at, string error, ExitCode code, string? now = null) =>
            new(OperationResult<IdeaRecord>.Fail(error, code), at, now);

        // In the order the single-entry methods always refused: what the
        // change says on its face, then a credential, then the workspace.
        for (var at = 0; at < edits.Count; at++)
        {
            if (edits[at].Refusal is { } refusal)
            {
                return Refused(at, refusal, ExitCode.InvalidArguments);
            }
        }

        for (var at = 0; at < edits.Count; at++)
        {
            if (Screened(edits[at].Screen) is { } screened)
            {
                return Refused(at, screened, ExitCode.PolicyViolation);
            }
        }

        if (Unavailable() is { } missing)
        {
            return Refused(null, missing, ExitCode.WorkspaceSyncFailed);
        }

        var path = RecordPath(place);

        if (!File.Exists(path))
        {
            return Refused(null, NotFound(place), ExitCode.InvalidArguments);
        }

        var held = await _yaml.HoldAsync([path], ct).ConfigureAwait(false);

        if (held.Failed)
        {
            return Refused(null, held.Error!, held.ExitCode);
        }

        using var hold = held.Value!;

        var read = await _kept.ReadAsync(path, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return Refused(null, read.Error!, read.ExitCode);
        }

        if (revision is not null && revision != read.Value!.Revision)
        {
            return Refused(null, Stale(place, revision, read.Value.Revision), ExitCode.ChangedSinceRead, read.Value.Revision);
        }

        var record = read.Value!.Value;

        // Made to the copy that was read, so a refusal part way through
        // leaves the file as it was: nothing is written unless all of them
        // could be made.
        for (var at = 0; at < edits.Count; at++)
        {
            if (edits[at].Change(record) is { } refused)
            {
                return Refused(at, refused, ExitCode.InvalidArguments, read.Value.Revision);
            }
        }

        var written = await hold.WriteAsync(path, record, true, ct).ConfigureAwait(false);

        if (written.Failed)
        {
            return Refused(null, written.Error!, written.ExitCode, read.Value.Revision);
        }

        _kept.Remember(path, record, written.Value!);

        return new IdeaEdited(OperationResult<IdeaRecord>.Ok(record), null, written.Value);
    }

    private static string Stale(IdeaPlace place, string expected, string? now) =>
        $"{place.Id} has changed since it was read: this was made from revision {expected} and it is at {now} now. "
        + "Read it again and make the change from what it says.";

    /// <summary>A record nothing else holds, so a caller changing what it was given changes nothing kept.</summary>
    /// <remarks>
    /// Copied through JSON, whole, so a field added to the record later is
    /// copied without anybody remembering to; a test checks a full record
    /// comes back equal.
    /// </remarks>
    internal static IdeaRecord Copy(IdeaRecord record) =>
        JsonSerializer.Deserialize<IdeaRecord>(JsonSerializer.SerializeToUtf8Bytes(record))!;

    private static IdeaOutcome Failed(int index, IdeaOperation operation, string error, ExitCode code) =>
        new(index, operation.Op, operation.Project, operation.Id?.Trim() ?? string.Empty, error, code, null, null);
}

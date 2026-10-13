using System.Globalization;
using System.Text.RegularExpressions;
using Loadout.Core.Git;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>Whether a mark still vouches for the text it was made on.</summary>
public enum VoiceMarkState
{
    /// <summary>The text is as it was when it was marked.</summary>
    Holds,

    /// <summary>The text has changed since, so the mark no longer counts until it is re-confirmed.</summary>
    Lapsed,

    /// <summary>The file or repository is not there to read.</summary>
    Missing,
}

/// <summary>A mark and where it stands.</summary>
public sealed record VoiceMarkStanding(VoiceMark Mark, VoiceMarkState State);

/// <summary>Something that could be marked, with the text it would be pinned to.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Reference">The commit's hash, the file's path relative to its repository, or <c>#123</c> for a pull request.</param>
/// <param name="Text">The text the mark would vouch for.</param>
public sealed record VoiceMarkCandidate(VoiceMarkKind Kind, string Reference, string Text)
{
    /// <summary>The first line, for a person deciding whether it is theirs.</summary>
    public string Summary => Text.Split('\n', 2)[0].Trim();
}

/// <summary>
/// Marking writing as hand-written, and telling which marks still hold.
/// </summary>
/// <remarks>
/// <para>
/// A mark is pinned to a hash of the text and lapses when the text changes.
/// That is simple, and it cannot be wrong without anybody noticing, which a
/// guess about how much of an edited file is still the person's could be.
/// </para>
/// <para>
/// A commit's mark never lapses. Its message is part of what its hash is
/// made from, so a different message is a different commit; what can happen
/// is that the commit stops existing, after a rebase, and that reads as
/// missing rather than lapsed.
/// </para>
/// <para>
/// A pull request's mark is pinned to its title and description, read through
/// gh, and lapses when either is edited. Reading one needs gh and the network,
/// so a pull request that cannot be read right now reads as missing, the same
/// as a file that is not there.
/// </para>
/// </remarks>
public sealed partial class VoiceMarks
{
    private readonly IGitManager _git;
    private readonly IPullRequestReader _pullRequests;
    private readonly VoiceStore _store;
    private readonly TimeProvider _time;

    public VoiceMarks(IGitManager git, IPullRequestReader pullRequests, VoiceStore store, TimeProvider time)
    {
        _git = git;
        _pullRequests = pullRequests;
        _store = store;
        _time = time;
    }

    /// <summary>
    /// <c>#123</c> or <c>pr:123</c>. Both, because a shell reads a word that
    /// starts with <c>#</c> as a comment unless it is quoted.
    /// </summary>
    [GeneratedRegex(@"^(?:#|pr:)(\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex PullRequestTarget();

    /// <summary>The pull request a target names, or null when it names something else.</summary>
    public static int? PullRequestNumber(string target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var match = PullRequestTarget().Match(target.Trim());

        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>How a mark names a pull request.</summary>
    public static string PullRequestReference(int number) => "#" + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// What a reference names: one file, one pull request, one commit, or every commit in a range.
    /// </summary>
    /// <param name="repository">The repository's root.</param>
    /// <param name="target">A file path, a pull request as <c>#123</c> or <c>pr:123</c>, a commit, or a range such as <c>main~20..main</c>.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<OperationResult<IReadOnlyList<VoiceMarkCandidate>>> CandidatesAsync(
        string repository,
        string target,
        CancellationToken ct = default)
    {
        var path = Path.GetFullPath(Path.Combine(repository, target));

        // A file first, whatever its name looks like: "../notes.md" has two
        // dots in it and is still a path, and a range never names a file.
        if (File.Exists(path))
        {
            if (!IsWithin(repository, path))
            {
                return OperationResult<IReadOnlyList<VoiceMarkCandidate>>.Fail(
                    $"'{target}' is outside the repository, so it cannot be marked from here.",
                    ExitCode.InvalidArguments);
            }

            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            return OperationResult<IReadOnlyList<VoiceMarkCandidate>>.Ok(
                [new VoiceMarkCandidate(VoiceMarkKind.File, Relative(repository, path), text)]);
        }

        if (PullRequestNumber(target) is { } number)
        {
            var read = await _pullRequests.ReadAsync(repository, number, ct).ConfigureAwait(false);

            return read.Failed
                ? OperationResult<IReadOnlyList<VoiceMarkCandidate>>.Fail(read.Error!, read.ExitCode)
                : OperationResult<IReadOnlyList<VoiceMarkCandidate>>.Ok(
                    [new VoiceMarkCandidate(VoiceMarkKind.PullRequest, PullRequestReference(number), read.Value!.Text)]);
        }

        var commits = await _git.ReadCommitMessagesAsync(repository, target, ct: ct).ConfigureAwait(false);

        if (commits.Failed)
        {
            return OperationResult<IReadOnlyList<VoiceMarkCandidate>>.Fail(
                $"'{target}' is neither a file in the repository nor a commit it knows: {commits.Error}",
                ExitCode.InvalidArguments);
        }

        return OperationResult<IReadOnlyList<VoiceMarkCandidate>>.Ok(commits.Value!
            .Select(c => new VoiceMarkCandidate(VoiceMarkKind.Commit, c.Sha, c.Message))
            .ToList());
    }

    /// <summary>
    /// Marks each candidate, or re-confirms it when it is already marked,
    /// pinning it to its text as it is now.
    /// </summary>
    /// <returns>How many were new and how many were re-confirmed.</returns>
    public async Task<OperationResult<(int Added, int Confirmed)>> MarkAsync(
        string repository,
        IReadOnlyList<VoiceMarkCandidate> candidates,
        CancellationToken ct = default)
    {
        var read = await _store.ReadMarksAsync(ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<(int, int)>.Fail(read.Error!, read.ExitCode);
        }

        var list = read.Value!;
        var root = Path.GetFullPath(repository);
        var now = _time.GetUtcNow();
        int added = 0, confirmed = 0;

        foreach (var candidate in candidates)
        {
            var hash = VoiceStore.Hash(candidate.Text);

            if (Find(list, root, candidate.Kind, candidate.Reference) is { } existing)
            {
                existing.Hash = hash;
                existing.Marked = now;
                confirmed++;

                continue;
            }

            list.Marks.Add(new VoiceMark
            {
                Kind = candidate.Kind,
                Reference = candidate.Reference,
                Repository = root,
                Hash = hash,
                Marked = now,
            });

            added++;
        }

        var written = await _store.WriteMarksAsync(list, ct).ConfigureAwait(false);

        return written.Failed
            ? OperationResult<(int, int)>.Fail(written.Error!, written.ExitCode)
            : OperationResult<(int, int)>.Ok((added, confirmed));
    }

    /// <summary>Takes marks off. Returns how many there were.</summary>
    public async Task<OperationResult<int>> UnmarkAsync(
        string repository,
        IReadOnlyList<VoiceMarkCandidate> candidates,
        CancellationToken ct = default)
    {
        var read = await _store.ReadMarksAsync(ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<int>.Fail(read.Error!, read.ExitCode);
        }

        var list = read.Value!;
        var root = Path.GetFullPath(repository);

        var removed = candidates
            .Select(c => Find(list, root, c.Kind, c.Reference))
            .OfType<VoiceMark>()
            .Distinct()
            .Count(list.Marks.Remove);

        if (removed == 0)
        {
            return OperationResult<int>.Ok(0);
        }

        var written = await _store.WriteMarksAsync(list, ct).ConfigureAwait(false);

        return written.Failed
            ? OperationResult<int>.Fail(written.Error!, written.ExitCode)
            : OperationResult<int>.Ok(removed);
    }

    /// <summary>Every mark, with whether it still holds.</summary>
    /// <param name="repository">Only this repository's, or every mark when null.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<OperationResult<IReadOnlyList<VoiceMarkStanding>>> StandingAsync(
        string? repository,
        CancellationToken ct = default)
    {
        var read = await _store.ReadMarksAsync(ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<IReadOnlyList<VoiceMarkStanding>>.Fail(read.Error!, read.ExitCode);
        }

        var root = repository is null ? null : Path.GetFullPath(repository);
        var standings = new List<VoiceMarkStanding>();

        foreach (var mark in read.Value!.Marks)
        {
            if (root is not null && !SamePath(mark.Repository, root))
            {
                continue;
            }

            standings.Add(new VoiceMarkStanding(mark, await StateAsync(mark, ct).ConfigureAwait(false)));
        }

        return OperationResult<IReadOnlyList<VoiceMarkStanding>>.Ok(standings);
    }

    /// <summary>
    /// The marks that have lapsed, as candidates carrying the text as it is
    /// now, ready to be re-confirmed.
    /// </summary>
    public async Task<OperationResult<IReadOnlyList<VoiceMarkCandidate>>> LapsedAsync(
        string repository,
        CancellationToken ct = default)
    {
        var standing = await StandingAsync(repository, ct).ConfigureAwait(false);

        if (standing.Failed)
        {
            return OperationResult<IReadOnlyList<VoiceMarkCandidate>>.Fail(standing.Error!, standing.ExitCode);
        }

        var lapsed = new List<VoiceMarkCandidate>();

        foreach (var found in standing.Value!.Where(s => s.State == VoiceMarkState.Lapsed))
        {
            if (await TextAsync(found.Mark, ct).ConfigureAwait(false) is { } text)
            {
                lapsed.Add(new VoiceMarkCandidate(found.Mark.Kind, found.Mark.Reference, text));
            }
        }

        return OperationResult<IReadOnlyList<VoiceMarkCandidate>>.Ok(lapsed);
    }

    /// <summary>
    /// The text a mark points at as it is now, or null when it cannot be read.
    /// </summary>
    public async Task<string?> TextAsync(VoiceMark mark, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mark);

        if (!Directory.Exists(mark.Repository))
        {
            return null;
        }

        switch (mark.Kind)
        {
            case VoiceMarkKind.Commit:
                var commit = await _git.ReadCommitMessagesAsync(mark.Repository, mark.Reference, ct: ct).ConfigureAwait(false);

                return commit.Succeeded && commit.Value!.Count == 1 ? commit.Value[0].Message : null;

            case VoiceMarkKind.PullRequest:
                if (PullRequestNumber(mark.Reference) is not { } number)
                {
                    return null;
                }

                var read = await _pullRequests.ReadAsync(mark.Repository, number, ct).ConfigureAwait(false);

                return read.Succeeded ? read.Value!.Text : null;

            default:
                var path = Path.Combine(mark.Repository, mark.Reference);

                return File.Exists(path) ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) : null;
        }
    }

    private async Task<VoiceMarkState> StateAsync(VoiceMark mark, CancellationToken ct)
    {
        if (await TextAsync(mark, ct).ConfigureAwait(false) is not { } text)
        {
            return VoiceMarkState.Missing;
        }

        // A commit's message is part of its hash, so a commit that can be read
        // at all holds.
        return mark.Kind == VoiceMarkKind.Commit
            || string.Equals(VoiceStore.Hash(text), mark.Hash, StringComparison.Ordinal)
            ? VoiceMarkState.Holds
            : VoiceMarkState.Lapsed;
    }

    private static VoiceMark? Find(VoiceMarkList list, string root, VoiceMarkKind kind, string reference) =>
        list.Marks.FirstOrDefault(m =>
            m.Kind == kind
            && SamePath(m.Repository, root)
            && string.Equals(m.Reference, reference, StringComparison.OrdinalIgnoreCase));

    /// <summary>Forward slashes, so a mark made on one platform names the same file on another.</summary>
    private static string Relative(string repository, string path) =>
        Path.GetRelativePath(Path.GetFullPath(repository), path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsWithin(string repository, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(repository), path);

        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

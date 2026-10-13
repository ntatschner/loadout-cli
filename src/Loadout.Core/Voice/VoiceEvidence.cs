using System.Globalization;
using System.Text.RegularExpressions;
using Loadout.Core.Git;
using Loadout.Core.Security;
using Loadout.Models.Results;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>One piece of the person's writing a run may learn from.</summary>
/// <param name="Id">Short id the agent cites it by, such as <c>e3</c>.</param>
/// <param name="Source">What kind of writing it is: <c>commit</c>, <c>doc</c>, <c>pr</c>, <c>corpus</c> or <c>prompt</c>.</param>
/// <param name="Reference">Where it came from: a commit hash, a path, a pull request as <c>#123</c>, or a session id.</param>
/// <param name="Text">The text, redacted.</param>
/// <param name="Hash">The hash of the text as written, so a citation can be told to still hold.</param>
public sealed record VoiceEvidence(string Id, string Source, string Reference, string Text, string Hash);

/// <summary>
/// A pull request's description and the squash message it was merged with:
/// what was drafted, and what the person kept.
/// </summary>
/// <param name="Number">The pull request.</param>
/// <param name="Commit">The squash commit.</param>
/// <param name="Before">The pull request's title and description, redacted.</param>
/// <param name="After">The squash message, redacted.</param>
public sealed record VoiceEditPair(int Number, string Commit, string Before, string After);

/// <summary>What gathering found.</summary>
/// <param name="Evidence">The pieces a run may read.</param>
/// <param name="Refused">
/// Pieces left out because redaction could not finish on them. Sent unchecked
/// they might carry a credential, so they are not sent at all.
/// </param>
/// <param name="EditPairs">Descriptions the person rewrote when they merged, when pull requests were asked for.</param>
public sealed record VoiceGathering(IReadOnlyList<VoiceEvidence> Evidence, int Refused, IReadOnlyList<VoiceEditPair> EditPairs)
{
    /// <summary>Nothing found.</summary>
    public VoiceGathering(IReadOnlyList<VoiceEvidence> evidence, int refused)
        : this(evidence, refused, [])
    {
    }
}

/// <summary>What to gather, and from where.</summary>
/// <param name="Repository">The repository, or null to read no repository.</param>
/// <param name="Corpus">A directory of somebody's writing, for a named style, or null.</param>
/// <param name="Sources">The source kinds the guide reads, such as <c>squash</c> and <c>mark</c>.</param>
/// <param name="PullRequests">Read merged pull requests through gh for edit pairs.</param>
/// <param name="Transcripts">Read what the person typed to Claude Code.</param>
public sealed record VoiceGatherRequest(
    string? Repository,
    string? Corpus,
    IReadOnlyCollection<string> Sources,
    bool PullRequests = false,
    bool Transcripts = false);

/// <summary>
/// Gathers the writing a learn run may read: only what is known to be the
/// person's own.
/// </summary>
/// <remarks>
/// <para>
/// Four kinds, and nothing else. Squash-merge messages, because they are
/// written by hand when a pull request is merged; anything the person has
/// marked, and only while its mark holds; for a named style, a corpus they
/// pointed at; and, when asked for, what they typed to Claude Code. The rest
/// of a repository was written by tools, agents and other people, and
/// learning from it would learn their voice.
/// </para>
/// <para>
/// Only the kinds the guide lists are read. A guide about word choice that
/// does not list squash merges is not handed them.
/// </para>
/// <para>
/// Edit pairs come only when asked for, since reading them goes to GitHub.
/// They are not evidence: a description the person rewrote is what they did
/// not want, and it is kept for scoring rules against rather than learning
/// them from.
/// </para>
/// <para>
/// Text is redacted before anything sees it. It is going to a model and its
/// excerpts are going to be stored, and a credential belongs in neither.
/// </para>
/// </remarks>
public sealed partial class VoiceEvidenceGatherer
{
    /// <summary>Commits read back from HEAD looking for squash merges.</summary>
    public const int SquashWalk = 300;

    /// <summary>The most of any one piece a run reads, so one long document cannot fill a call by itself.</summary>
    public const int PieceCharacters = 8_000;

    private static readonly string[] CorpusExtensions = [".md", ".txt", ".markdown"];

    private readonly IGitManager _git;
    private readonly IPullRequestReader _pullRequests;
    private readonly VoiceMarks _marks;
    private readonly VoiceTranscripts? _transcripts;

    public VoiceEvidenceGatherer(
        IGitManager git,
        IPullRequestReader pullRequests,
        VoiceMarks marks,
        VoiceTranscripts? transcripts = null)
    {
        _git = git;
        _pullRequests = pullRequests;
        _marks = marks;
        _transcripts = transcripts;
    }

    /// <summary>The source kind evidence typed to an agent is filed under.</summary>
    public const string Prompt = "prompt";

    /// <summary>The subject GitHub gives a squash merge: the title, then the pull request's number.</summary>
    [GeneratedRegex(@"\(#(\d{1,9})\)\s*$")]
    private static partial Regex SquashSubject();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// The writing in a repository known to be the person's, and a corpus if
    /// one is named.
    /// </summary>
    /// <param name="repository">The repository, or null to read only the corpus.</param>
    /// <param name="corpus">A directory of somebody's writing, for a named style, or null.</param>
    /// <param name="pullRequests">Read merged pull requests through gh for edit pairs.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<OperationResult<VoiceGathering>> GatherAsync(
        string? repository,
        string? corpus,
        bool pullRequests = false,
        CancellationToken ct = default) =>
        GatherAsync(new VoiceGatherRequest(repository, corpus, VoiceGuides.Prose().Sources, pullRequests), ct);

    /// <summary>
    /// The writing a guide's sources hold: in a repository, known to be the
    /// person's; a corpus if one is named; their prompts if asked for.
    /// </summary>
    public async Task<OperationResult<VoiceGathering>> GatherAsync(VoiceGatherRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (repository, corpus, sources, pullRequests, transcripts) = request;

        if (corpus is not null && !sources.Contains("corpus"))
        {
            return OperationResult<VoiceGathering>.Fail(
                "This guide does not read a corpus.", Models.ExitCode.InvalidArguments);
        }

        if (transcripts && !sources.Contains(VoiceGuideDrafting.Transcript))
        {
            return OperationResult<VoiceGathering>.Fail(
                "This guide does not read transcripts.", Models.ExitCode.InvalidArguments);
        }

        if (pullRequests && !sources.Contains("edit-pair"))
        {
            return OperationResult<VoiceGathering>.Fail(
                "This guide is not scored against edit pairs.", Models.ExitCode.InvalidArguments);
        }

        var pieces = new List<(string Source, string Reference, string Text)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var squashes = new List<(int Number, string Sha, string Message)>();

        if (repository is not null && sources.Contains("mark"))
        {
            var standing = await _marks.StandingAsync(repository, ct).ConfigureAwait(false);

            if (standing.Failed)
            {
                return OperationResult<VoiceGathering>.Fail(standing.Error!, standing.ExitCode);
            }

            foreach (var held in standing.Value!.Where(s => s.State == VoiceMarkState.Holds))
            {
                var mark = held.Mark;

                if (await _marks.TextAsync(mark, ct).ConfigureAwait(false) is { } text)
                {
                    pieces.Add((Source(mark.Kind), mark.Reference, text));
                }

                seen.Add(mark.Reference);
            }
        }

        // Squash merges are read for edit pairs even by a guide that does not
        // learn from them, since a pair is a pull request and its squash.
        if (repository is not null && (sources.Contains("squash") || pullRequests))
        {
            // A repository with no commits yet has nothing to walk, which is
            // an ordinary answer rather than a failure.
            var history = await _git.ReadCommitMessagesAsync(repository, "HEAD", SquashWalk, ct).ConfigureAwait(false);

            foreach (var (sha, message) in history.Value ?? [])
            {
                if (SquashSubject().Match(message.Split('\n', 2)[0]) is not { Success: true } squash)
                {
                    continue;
                }

                squashes.Add((int.Parse(squash.Groups[1].Value, CultureInfo.InvariantCulture), sha, message));

                if (sources.Contains("squash") && seen.Add(sha))
                {
                    pieces.Add(("commit", sha, message));
                }
            }
        }

        var pairs = new List<VoiceEditPair>();
        var refused = 0;

        if (pullRequests && repository is not null && squashes.Count > 0)
        {
            var merged = await _pullRequests.MergedAsync(repository, SquashWalk, ct).ConfigureAwait(false);

            if (merged.Failed)
            {
                return OperationResult<VoiceGathering>.Fail(merged.Error!, merged.ExitCode);
            }

            var byNumber = merged.Value!.GroupBy(p => p.Number).ToDictionary(g => g.Key, g => g.First());

            foreach (var (number, sha, message) in squashes)
            {
                // A description left empty was never drafted, and one merged
                // unchanged was kept as it was: neither shows a rewrite.
                if (!byNumber.TryGetValue(number, out var pull)
                    || pull.Body.Trim().Length == 0
                    || Flatten(pull.Text) == Flatten(message))
                {
                    continue;
                }

                try
                {
                    pairs.Add(new VoiceEditPair(number, sha, Clip(SecretRedactor.Redact(pull.Text)), Clip(SecretRedactor.Redact(message))));
                }
                catch (RegexMatchTimeoutException)
                {
                    refused++;
                }
            }
        }

        if (corpus is not null)
        {
            if (!Directory.Exists(corpus))
            {
                return OperationResult<VoiceGathering>.Fail(
                    $"There is no directory at '{corpus}' to read a corpus from.", Models.ExitCode.InvalidArguments);
            }

            foreach (var file in Directory.EnumerateFiles(corpus, "*", SearchOption.AllDirectories)
                .Where(f => CorpusExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(corpus, file).Replace(Path.DirectorySeparatorChar, '/');

                pieces.Add(("corpus", relative, await File.ReadAllTextAsync(file, ct).ConfigureAwait(false)));
            }
        }

        if (transcripts)
        {
            if (_transcripts is null)
            {
                return OperationResult<VoiceGathering>.Fail("Transcripts cannot be read here.");
            }

            foreach (var transcript in await _transcripts.ReadAsync(ct).ConfigureAwait(false))
            {
                pieces.Add((Prompt, transcript.Session, transcript.Text));
            }
        }

        var evidence = new List<VoiceEvidence>();

        foreach (var (source, reference, text) in pieces.Where(p => p.Text.Trim().Length > 0))
        {
            string redacted;

            try
            {
                redacted = SecretRedactor.Redact(text);
            }
            catch (RegexMatchTimeoutException)
            {
                refused++;

                continue;
            }

            evidence.Add(new VoiceEvidence(
                $"e{evidence.Count + 1}", source, reference, Clip(redacted), VoiceStore.Hash(text)));
        }

        return OperationResult<VoiceGathering>.Ok(new VoiceGathering(evidence, refused, pairs));
    }

    private static string Source(VoiceMarkKind kind) => kind switch
    {
        VoiceMarkKind.File => VoiceGuides.Document,
        VoiceMarkKind.PullRequest => VoiceGuides.PullRequest,
        _ => VoiceGuides.Commit,
    };

    private static string Flatten(string text) => Whitespace().Replace(text, " ").Trim();

    private static string Clip(string text) =>
        text.Length <= PieceCharacters ? text : text[..PieceCharacters];
}

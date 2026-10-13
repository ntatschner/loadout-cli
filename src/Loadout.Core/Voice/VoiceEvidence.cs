using System.Text.RegularExpressions;
using Loadout.Core.Git;
using Loadout.Core.Security;
using Loadout.Models.Results;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>One piece of the person's writing a run may learn from.</summary>
/// <param name="Id">Short id the agent cites it by, such as <c>e3</c>.</param>
/// <param name="Source">What kind of writing it is: <c>commit</c>, <c>doc</c> or <c>corpus</c>.</param>
/// <param name="Reference">Where it came from: a commit hash or a path.</param>
/// <param name="Text">The text, redacted.</param>
/// <param name="Hash">The hash of the text as written, so a citation can be told to still hold.</param>
public sealed record VoiceEvidence(string Id, string Source, string Reference, string Text, string Hash);

/// <summary>What gathering found.</summary>
/// <param name="Evidence">The pieces a run may read.</param>
/// <param name="Refused">
/// Pieces left out because redaction could not finish on them. Sent unchecked
/// they might carry a credential, so they are not sent at all.
/// </param>
public sealed record VoiceGathering(IReadOnlyList<VoiceEvidence> Evidence, int Refused);

/// <summary>
/// Gathers the writing a learn run may read: only what is known to be the
/// person's own.
/// </summary>
/// <remarks>
/// <para>
/// Three kinds, and nothing else. Squash-merge messages, because they are
/// written by hand when a pull request is merged; anything the person has
/// marked, and only while its mark holds; and, for a named style, a corpus
/// they pointed at. The rest of a repository was written by tools, agents
/// and other people, and learning from it would learn their voice.
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
    private readonly VoiceMarks _marks;

    public VoiceEvidenceGatherer(IGitManager git, VoiceMarks marks)
    {
        _git = git;
        _marks = marks;
    }

    /// <summary>The subject GitHub gives a squash merge: the title, then the pull request's number.</summary>
    [GeneratedRegex(@"\(#\d+\)\s*$")]
    private static partial Regex SquashSubject();

    /// <summary>
    /// The writing in a repository known to be the person's, and a corpus if
    /// one is named.
    /// </summary>
    /// <param name="repository">The repository, or null to read only the corpus.</param>
    /// <param name="corpus">A directory of somebody's writing, for a named style, or null.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<OperationResult<VoiceGathering>> GatherAsync(
        string? repository,
        string? corpus,
        CancellationToken ct = default)
    {
        var pieces = new List<(string Source, string Reference, string Text)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (repository is not null)
        {
            var standing = await _marks.StandingAsync(repository, ct).ConfigureAwait(false);

            if (standing.Failed)
            {
                return OperationResult<VoiceGathering>.Fail(standing.Error!, standing.ExitCode);
            }

            foreach (var held in standing.Value!.Where(s => s.State == VoiceMarkState.Holds))
            {
                var mark = held.Mark;

                if (mark.Kind == VoiceMarkKind.File)
                {
                    var text = await File.ReadAllTextAsync(Path.Combine(mark.Repository, mark.Reference), ct)
                        .ConfigureAwait(false);

                    pieces.Add(("doc", mark.Reference, text));
                }
                else
                {
                    var commit = await _git.ReadCommitMessagesAsync(mark.Repository, mark.Reference, ct: ct)
                        .ConfigureAwait(false);

                    if (commit.Succeeded && commit.Value!.Count == 1)
                    {
                        pieces.Add(("commit", mark.Reference, commit.Value[0].Message));
                    }
                }

                seen.Add(mark.Reference);
            }

            // A repository with no commits yet has nothing to walk, which is
            // an ordinary answer rather than a failure.
            var history = await _git.ReadCommitMessagesAsync(repository, "HEAD", SquashWalk, ct).ConfigureAwait(false);

            foreach (var (sha, message) in history.Value ?? [])
            {
                if (!seen.Contains(sha) && SquashSubject().IsMatch(message.Split('\n', 2)[0]))
                {
                    pieces.Add(("commit", sha, message));
                    seen.Add(sha);
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

        var evidence = new List<VoiceEvidence>();
        var refused = 0;

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

        return OperationResult<VoiceGathering>.Ok(new VoiceGathering(evidence, refused));
    }

    private static string Clip(string text) =>
        text.Length <= PieceCharacters ? text : text[..PieceCharacters];
}

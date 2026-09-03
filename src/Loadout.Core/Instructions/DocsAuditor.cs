using System.Text.RegularExpressions;

namespace Loadout.Core.Instructions;

/// <summary>Something in the documentation that no longer holds.</summary>
/// <param name="Path">The document, relative to the repository.</param>
/// <param name="Line">Line it is on, counting from one.</param>
/// <param name="Kind">Short name for the sort of problem, for grouping.</param>
/// <param name="Detail">What is wrong, in a sentence.</param>
public sealed record DocsFinding(string Path, int Line, string Kind, string Detail);

/// <summary>
/// Counts the places documentation has come adrift from the repository.
/// </summary>
/// <remarks>
/// <para>
/// Read-only, and deliberately so, in the same way the convention auditor is: it
/// reports, proposes no edit and makes none. What to do about a stale page is a
/// judgement about a codebase, and the point is to put the question in front of
/// somebody rather than answer it for them.
/// </para>
/// <para>
/// This is the first of the checks and the only one that needs nothing
/// configured. A reference either resolves or it does not — no policy, no
/// declared surface, no counting of things whose names have to be mapped to
/// something countable. That is why it comes first: it works on any repository
/// the day it is pointed at one.
/// </para>
/// <para>
/// The refusals matter as much as the checks. A path with a placeholder in it, a
/// home-relative path, a URL and an absolute path are all skipped, because a
/// checker that flags <c>projects/&lt;slug&gt;/memory/</c> as missing is a
/// checker somebody turns off. It is better to miss a broken reference than to
/// be wrong about a good one.
/// </para>
/// </remarks>
public static partial class DocsAuditor
{
    /// <summary>A Markdown link, capturing its target.</summary>
    [GeneratedRegex(@"\[[^\]]*\]\(([^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled)]
    private static partial Regex Link();

    /// <summary>
    /// Something in backticks that is shaped like a path within the repository.
    /// </summary>
    /// <remarks>
    /// A slash and a file extension, both required. One or the other alone
    /// catches far too much: prose says "input/output" and names files like
    /// <c>config.yaml</c> that live wherever the reader's is.
    /// </remarks>
    [GeneratedRegex(@"`([A-Za-z0-9_.\-]+(?:/[A-Za-z0-9_.\-]+)+\.[A-Za-z0-9]{1,10})`", RegexOptions.Compiled)]
    private static partial Regex CodePath();

    /// <summary>
    /// Audits a set of documents against the repository holding them.
    /// </summary>
    /// <param name="repositoryRoot">The repository the paths are relative to.</param>
    /// <param name="documents">Documents to read, relative to the root.</param>
    /// <param name="ct">Cancellation token.</param>
    public static IReadOnlyList<DocsFinding> Audit(
        string repositoryRoot,
        IReadOnlyList<string> documents,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var findings = new List<DocsFinding>();

        // Every document each one points at, so an orphan can be told from a
        // page that simply nothing has linked yet in this pass.
        var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var roots = TopLevelDirectories(repositoryRoot);

        foreach (var document in documents)
        {
            ct.ThrowIfCancellationRequested();

            var full = Path.Combine(repositoryRoot, Native(document));

            string[] lines;

            try
            {
                lines = File.ReadAllLines(full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                findings.Add(new DocsFinding(document, 0, "unreadable", $"could not be read: {ex.Message}"));

                continue;
            }

            for (var index = 0; index < lines.Length; index++)
            {
                Check(repositoryRoot, roots, document, lines[index], index + 1, findings, linked);
            }
        }

        findings.AddRange(Orphans(documents, linked));

        return findings;
    }

    private static void Check(
        string root,
        IReadOnlySet<string> roots,
        string document,
        string line,
        int number,
        List<DocsFinding> findings,
        HashSet<string> linked)
    {
        var directory = Path.GetDirectoryName(document) ?? string.Empty;

        foreach (Match match in Link().Matches(line))
        {
            var target = match.Groups[1].Value;

            if (!IsCheckable(target))
            {
                continue;
            }

            // A link to a heading within a page is a link to the page.
            var anchor = target.IndexOf('#', StringComparison.Ordinal);
            var path = anchor >= 0 ? target[..anchor] : target;

            if (path.Length == 0)
            {
                continue;
            }

            // Relative to the document, which is how a reader's browser and
            // every Markdown renderer resolve it.
            var resolved = Normalise(Path.Combine(directory, path));

            if (Exists(root, resolved))
            {
                linked.Add(resolved);

                continue;
            }

            findings.Add(new DocsFinding(
                document, number, "broken-link", $"links to '{target}', which is not there."));
        }

        foreach (Match match in CodePath().Matches(line))
        {
            var path = match.Groups[1].Value;

            if (!IsCheckable(path) || !IsAboutThisRepository(path, roots) || Exists(root, path))
            {
                continue;
            }

            findings.Add(new DocsFinding(
                document,
                number,
                "missing-path",
                $"names '{path}', which is not in the repository."));
        }
    }

    /// <summary>
    /// Documents nothing else points at.
    /// </summary>
    /// <remarks>
    /// A page nobody links is a page nobody arrives at, whatever is in it. The
    /// root README is never an orphan — it is where a reader starts — and
    /// neither is a directory's own index, which is the thing doing the linking.
    /// </remarks>
    private static IEnumerable<DocsFinding> Orphans(
        IReadOnlyList<string> documents,
        HashSet<string> linked) =>
        documents
            .Where(document => !linked.Contains(document) && !IsEntryPoint(document))
            .Select(document => new DocsFinding(
                document, 0, "orphan", "nothing links to it, so nobody arrives at it."));

    private static bool IsEntryPoint(string document) =>
        Path.GetFileName(document).Equals("README.md", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this is a reference into the repository at all.
    /// </summary>
    /// <remarks>
    /// Everything refused here is refused because being wrong about it is worse
    /// than missing it. A placeholder is not a path, a home-relative path is
    /// about the reader's machine rather than this repository, and an absolute
    /// path is about somebody's disk.
    /// </remarks>
    private static bool IsCheckable(string target) =>
        target.Length > 0
        && !target.StartsWith('#')
        && !target.StartsWith('~')
        && !target.StartsWith('/')
        && !target.StartsWith('\\')
        && !target.Contains("://", StringComparison.Ordinal)
        && !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        && target.IndexOfAny(['<', '>', '*', '$', '{', '|', '?']) < 0
        && !(target.Length > 1 && target[1] == ':');

    /// <summary>
    /// Whether backticked text shaped like a path is a claim about this
    /// repository, rather than prose that happens to look like one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pointed at this repository's own documentation, the first version of this
    /// check produced seven findings and every one was wrong. One was an
    /// invented Rust path inside a paragraph explaining how globs get derived
    /// from headings. The other six were a table whose column gives paths
    /// relative to <c>src/&lt;project&gt;</c> — real files, addressed from
    /// somewhere other than the root.
    /// </para>
    /// <para>
    /// Neither is a broken reference, and a check that is wrong about seven good
    /// ones and right about none is a check that gets switched off. Requiring
    /// the first segment to be a directory this repository actually has at its
    /// root is what tells the two apart: <c>src/Loadout.Core/Gone.cs</c> is a
    /// claim about this repository and is worth checking;
    /// <c>crates/core/src/store.rs</c> is somebody's example.
    /// </para>
    /// <para>
    /// Resolving a path as a suffix of any file in the tree was the other way to
    /// do it, and would have quietly accepted the invented one the moment a file
    /// of that name appeared anywhere.
    /// </para>
    /// </remarks>
    private static bool IsAboutThisRepository(string path, IReadOnlySet<string> roots) =>
        path.Split('/', 2) is [{ Length: > 0 } first, _] && roots.Contains(first);

    /// <summary>
    /// The directories at the top of the repository, which is what a path in the
    /// documentation has to begin with to be about it.
    /// </summary>
    private static IReadOnlySet<string> TopLevelDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root)
                .Select(directory => Path.GetFileName(directory))
                .Where(name => name is { Length: > 0 } && !name.StartsWith('.'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the list nothing is checkable, which reports nothing
            // rather than reporting everything as missing.
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool Exists(string root, string relative)
    {
        var full = Path.Combine(root, Native(relative));

        return File.Exists(full) || Directory.Exists(full);
    }

    private static string Native(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>A path with any <c>..</c> segments resolved, still relative.</summary>
    private static string Normalise(string path)
    {
        var segments = new List<string>();

        foreach (var segment in path.Split(
            ['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 0 && segments[^1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);

                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }
}

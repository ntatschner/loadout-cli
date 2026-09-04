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
    /// A claim of the form "73 specialists", with the number and what it counts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Digits and the small words, because prose writes both — "there are 73
    /// specialists" and "all six runtime identifiers" are the same claim and
    /// only one of them is a numeral.
    /// </para>
    /// <para>
    /// One is left out, and it has to be. "The full text of one specialist" is a
    /// quantity in a sentence rather than a claim about a total, and prose is
    /// full of them; a total that genuinely is one is rare and hardly worth
    /// checking. Missing that is cheaper than being wrong about every "one
    /// specialist" on the page.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"\b([2-9]|[1-9]\d{1,3}|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s+([a-z][a-z\-]{2,30})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex Claim();

    /// <summary>
    /// Audits a set of documents against the repository holding them.
    /// </summary>
    /// <param name="repositoryRoot">The repository the paths are relative to.</param>
    /// <param name="documents">Documents to read, relative to the root.</param>
    /// <param name="policy">
    /// What the project says its prose can be checked against, or null when it
    /// has said nothing. Without one the reference checks still run: they need
    /// no configuration, because a link either resolves or it does not.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static IReadOnlyList<DocsFinding> Audit(
        string repositoryRoot,
        IReadOnlyList<string> documents,
        Models.Instructions.DocsPolicy? policy = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var findings = new List<DocsFinding>();

        // Every document each one points at, so an orphan can be told from a
        // page that simply nothing has linked yet in this pass.
        var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var roots = TopLevelDirectories(repositoryRoot);

        // Counted once for the whole audit rather than per page. A glob over a
        // repository is not free, and every page asking "how many specialists"
        // has to get the same answer or the report contradicts itself.
        var counts = Counted(repositoryRoot, policy, ct);

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

            // A page the policy has set aside is still checked for references —
            // its links either resolve or they do not, whatever its numbers are
            // about — and only its numbers are left alone.
            var countable = IsCountable(document, policy) ? counts : Nothing;

            for (var index = 0; index < lines.Length; index++)
            {
                Check(repositoryRoot, roots, countable, document, lines[index], index + 1, findings, linked);
            }
        }

        findings.AddRange(Orphans(documents, linked));

        return findings;
    }

    private static void Check(
        string root,
        IReadOnlySet<string> roots,
        IReadOnlyDictionary<string, int> counts,
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

        if (counts.Count == 0)
        {
            return;
        }

        foreach (Match match in Claim().Matches(line))
        {
            var noun = Singular(match.Groups[2].Value.ToLowerInvariant());

            if (!counts.TryGetValue(noun, out var actual)
                || Number(match.Groups[1].Value) is not { } claimed
                || claimed == actual)
            {
                continue;
            }

            findings.Add(new DocsFinding(
                document,
                number,
                "wrong-count",
                $"says {match.Groups[1].Value} {match.Groups[2].Value}, and there are {actual}."));
        }
    }

    /// <summary>No counts at all, for a page whose numbers are somebody else's.</summary>
    private static readonly IReadOnlyDictionary<string, int> Nothing =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this page's numbers are about this repository.</summary>
    private static bool IsCountable(string document, Models.Instructions.DocsPolicy? policy) =>
        policy is not { CountsExclude.Count: > 0 }
        || !policy.CountsExclude.Any(excluded =>
            document.EndsWith(excluded.Replace('\\', '/').TrimStart('/'), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What each counted noun actually comes to, worked out once.
    /// </summary>
    /// <remarks>
    /// Counted against the repository the documentation is in, using the same
    /// glob matcher scoped rules use, so a pattern written in a policy means the
    /// same thing as one written in a rule.
    /// </remarks>
    private static IReadOnlyDictionary<string, int> Counted(
        string root,
        Models.Instructions.DocsPolicy? policy,
        CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (policy is not { Counts.Count: > 0 })
        {
            return counts;
        }

        List<string> files;

        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                // Build output and other people's code are not what a count in
                // prose is ever about, and a bin directory would swamp any of
                // these patterns.
                .Where(file => !file.Contains("/bin/", StringComparison.Ordinal)
                    && !file.Contains("/obj/", StringComparison.Ordinal)
                    && !file.StartsWith(".git/", StringComparison.Ordinal)
                    && !file.Contains("/node_modules/", StringComparison.Ordinal))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing countable means nothing claimed about, which reports
            // nothing rather than reporting every number as wrong.
            return counts;
        }

        foreach (var (noun, glob) in policy.Counts)
        {
            ct.ThrowIfCancellationRequested();

            counts[Singular(noun.ToLowerInvariant())] =
                files.Count(file => RuleService.Matches(glob, file));
        }

        return counts;
    }

    /// <summary>
    /// A noun reduced to the form the policy is keyed by.
    /// </summary>
    /// <remarks>
    /// Crude on purpose. A policy names the singular and prose writes the
    /// plural, and stripping a trailing "s" covers what documentation actually
    /// says. Anything cleverer is a stemmer, and a stemmer that is wrong about
    /// one word in fifty is a report that is wrong about one line in fifty.
    /// </remarks>
    private static string Singular(string noun) =>
        noun.Length > 3 && noun.EndsWith('s') && !noun.EndsWith("ss", StringComparison.Ordinal)
            ? noun[..^1]
            : noun;

    /// <summary>A claimed quantity, whether it was written as digits or a word.</summary>
    private static int? Number(string text) =>
        int.TryParse(text, out var value)
            ? value
            : text.ToLowerInvariant() switch
            {
                "one" => 1,
                "two" => 2,
                "three" => 3,
                "four" => 4,
                "five" => 5,
                "six" => 6,
                "seven" => 7,
                "eight" => 8,
                "nine" => 9,
                "ten" => 10,
                "eleven" => 11,
                "twelve" => 12,
                _ => null,
            };

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

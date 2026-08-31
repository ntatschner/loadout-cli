using System.Text.RegularExpressions;

namespace Loadout.Core.Instructions;

/// <summary>One thing a specialist asks for that can be counted in a repository.</summary>
/// <param name="SpecialistId">The specialist whose rule this is.</param>
/// <param name="Rule">The rule, quoted from that specialist so the report can show its source.</param>
/// <param name="Extension">File extension the check reads, including the dot.</param>
/// <param name="Pattern">What counts as an occurrence.</param>
/// <param name="Caveat">What the check cannot see, said in the report rather than hidden.</param>
public sealed record ConventionCheck(
    string SpecialistId,
    string Rule,
    string Extension,
    Regex Pattern,
    string Caveat);

/// <summary>Where a repository departs from what its specialists ask for.</summary>
/// <param name="Check">The rule that was counted.</param>
/// <param name="Occurrences">How many times it was found.</param>
/// <param name="Files">The files holding them, worst first, with their counts.</param>
/// <param name="FilesRead">How many files of that kind were examined.</param>
public sealed record ConventionFinding(
    ConventionCheck Check,
    int Occurrences,
    IReadOnlyList<(string Path, int Count)> Files,
    int FilesRead);

/// <summary>
/// Counts where a repository does something its own specialists advise against.
/// </summary>
/// <remarks>
/// <para>
/// Read-only, and deliberately so. It reports; it proposes no edit and makes
/// none. What to do about a deviation is a judgement about a codebase, and the
/// point of this is to put the question in front of somebody rather than answer
/// it for them.
/// </para>
/// <para>
/// Every check quotes the rule it came from. A count with no source behind it
/// is an opinion the tool cannot defend, and the specialist libraries are where
/// these opinions are supposed to live — not in here.
/// </para>
/// <para>
/// Only measurable things. "Use approved verbs" and "think in sets" are good
/// rules and cannot be counted, so they are not attempted: a check that is
/// nearly right produces findings nobody trusts, and one untrusted finding
/// costs more attention than the ten true ones beside it earn.
/// </para>
/// </remarks>
public static class ConventionAuditor
{
    /// <summary>Directories no audit should read.</summary>
    private static readonly string[] Ignored =
        [".git", "node_modules", "target", "bin", "obj", "dist", "vendor", ".venv"];

    /// <summary>How many files to name for a finding before the list stops being read.</summary>
    private const int FilesNamed = 5;

    /// <summary>
    /// The checks that ship. Small on purpose, and each one quotes its rule.
    /// </summary>
    public static IReadOnlyList<ConventionCheck> Checks { get; } =
    [
        new ConventionCheck(
            "language.rust",
            "`unwrap()` in library code is a panic waiting for a user.",
            ".rs",
            new Regex(@"\.unwrap\(\)", RegexOptions.Compiled),
            "Test code is excluded by file name and path only. A #[cfg(test)] "
            + "module inside a source file is still counted."),

        new ConventionCheck(
            "language.sql",
            "Name columns in `INSERT` and never rely on `SELECT *` in application code.",
            ".sql",
            new Regex(@"\bSELECT\s+\*", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "Migrations and ad-hoc scripts are counted the same as application "
            + "queries, and the rule is about application code."),

        new ConventionCheck(
            "language.powershell",
            "`-ErrorAction SilentlyContinue` hides the message, not the failure.",
            ".ps1",
            new Regex(@"-ErrorAction\s+SilentlyContinue", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "Some of these are deliberate; the rule allows for a failure that is "
            + "genuinely fine, said with try and catch."),
    ];

    /// <summary>
    /// Counts every check against a repository, reporting only what it found.
    /// </summary>
    /// <param name="repositoryPath">The clone to read.</param>
    /// <param name="applies">
    /// Whether a specialist applies to this project. A check for a language the
    /// repository is not written in is noise, and its specialist would not be
    /// loaded either.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static IReadOnlyList<ConventionFinding> Audit(
        string repositoryPath,
        Func<string, bool> applies,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(applies);

        var findings = new List<ConventionFinding>();

        foreach (var check in Checks)
        {
            ct.ThrowIfCancellationRequested();

            if (!applies(check.SpecialistId))
            {
                continue;
            }

            var counted = new List<(string Path, int Count)>();
            var total = 0;
            var read = 0;

            foreach (var file in Files(repositoryPath, check.Extension))
            {
                ct.ThrowIfCancellationRequested();

                if (check.Extension == ".rs" && IsRustTest(file))
                {
                    continue;
                }

                read++;

                string text;

                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    // A file that cannot be read is not a finding.
                    continue;
                }

                var count = check.Pattern.Matches(text).Count;

                if (count == 0)
                {
                    continue;
                }

                total += count;
                counted.Add((Path.GetRelativePath(repositoryPath, file), count));
            }

            if (total == 0)
            {
                continue;
            }

            findings.Add(new ConventionFinding(
                check,
                total,
                [.. counted.OrderByDescending(f => f.Count).ThenBy(f => f.Path, StringComparer.Ordinal).Take(FilesNamed)],
                read));
        }

        return findings;
    }

    /// <summary>
    /// Whether a Rust file is test code, by name and path.
    /// </summary>
    /// <remarks>
    /// Crude, and the report says so. Rust puts unit tests inside the file they
    /// test behind #[cfg(test)], which this does not see — so a count is a
    /// ceiling rather than a verdict, and saying that is the difference between
    /// a number somebody acts on and one they learn to ignore.
    /// </remarks>
    private static bool IsRustTest(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        return name.EndsWith("_test", StringComparison.Ordinal)
            || name.EndsWith("_tests", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}benches{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static IEnumerable<string> Files(string root, string extension)
    {
        IEnumerable<string> found;

        try
        {
            found = Directory.EnumerateFiles(root, "*" + extension, SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in found)
        {
            var relative = Path.GetRelativePath(root, file);

            if (Ignored.Any(directory =>
                relative.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || relative.Contains(
                    $"{Path.DirectorySeparatorChar}{directory}{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return file;
        }
    }
}

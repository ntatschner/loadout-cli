using System.Text.RegularExpressions;

namespace Loadout.Core.Instructions;

/// <summary>One thing about a repository that can be counted rather than judged.</summary>
/// <param name="Subject">What was looked for, as a person would name it.</param>
/// <param name="Finding">What was found.</param>
/// <param name="Evidence">How many files said so, so a reader can weigh it.</param>
public sealed record ProjectConvention(string Subject, string Finding, int Evidence);

/// <summary>
/// Counts what a repository does, so a project specialist does not start from a
/// blank page.
/// </summary>
/// <remarks>
/// <para>
/// The built-in library knows about "C#" and ".NET". What it cannot know is that
/// <em>this</em> repository returns a result type rather than throwing, names
/// its tests in a particular shape, and explains its non-obvious choices in a
/// doc comment rather than a line comment. That is the guidance that stops an
/// agent writing code which reads as foreign, and until now somebody had to
/// notice it and write it out by hand.
/// </para>
/// <para>
/// Only what can be counted. Which test framework is referenced, whether
/// nullable is on, how often a result type is returned against how often
/// something is thrown — each has a number behind it and a caveat a reader can
/// weigh. Everything else about a codebase is a judgement, and a judgement
/// dressed as a measurement is worse than an empty section: the section says it
/// needs writing, and the measurement says it is already done.
/// </para>
/// <para>
/// Bounded like the evidence reader next door. This runs when somebody drafts a
/// specialist, not on every launch, but a repository can be enormous and a
/// scaffold that takes a minute is one nobody waits for.
/// </para>
/// </remarks>
public static partial class ProjectConventions
{
    /// <summary>How many files to read before the answer stops changing.</summary>
    private const int MostFiles = 600;

    /// <summary>Directories holding code nobody here wrote.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", "dist", "build", "out", "target",
        "vendor", ".venv", "venv", "__pycache__", "packages", "coverage",
    };

    [GeneratedRegex(@"\breturn\s+(?:Operation)?Result[<.]", RegexOptions.Compiled)]
    private static partial Regex ResultReturn();

    [GeneratedRegex(@"\bthrow\s+new\b", RegexOptions.Compiled)]
    private static partial Regex Throw();

    [GeneratedRegex(@"^\s*///", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex DocComment();

    /// <summary>
    /// What this repository can be seen to do.
    /// </summary>
    /// <param name="repositoryPath">The repository to read.</param>
    /// <param name="ct">Cancellation token.</param>
    public static IReadOnlyList<ProjectConvention> Detect(
        string repositoryPath,
        CancellationToken ct = default)
    {
        var found = new List<ProjectConvention>();

        if (!Directory.Exists(repositoryPath))
        {
            return found;
        }

        var sources = new List<string>();
        var manifests = new List<string>();

        foreach (var file in Walk(repositoryPath, ct))
        {
            var name = Path.GetFileName(file);

            if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase))
            {
                manifests.Add(file);
            }
            else if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(file);
            }
        }

        if (sources.Count == 0 && manifests.Count == 0)
        {
            return found;
        }

        var manifestText = Read(manifests, ct);

        Add(found, "Test framework", Framework(manifestText), manifests.Count);
        Add(found, "Assertions", Assertions(manifestText), manifests.Count);
        Add(found, "Nullable reference types", Nullable(manifestText), manifests.Count);
        Add(found, "Warnings as errors", WarningsAsErrors(manifestText), manifests.Count);

        found.AddRange(FromSources(sources, ct));

        return found;
    }

    private static IEnumerable<ProjectConvention> FromSources(
        IReadOnlyList<string> sources,
        CancellationToken ct)
    {
        var results = 0;
        var throws = 0;
        var documented = 0;
        var read = 0;

        foreach (var file in sources)
        {
            ct.ThrowIfCancellationRequested();

            string text;

            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            read++;
            results += ResultReturn().Count(text);
            throws += Throw().Count(text);

            if (DocComment().IsMatch(text))
            {
                documented++;
            }
        }

        if (read == 0)
        {
            yield break;
        }

        // Stated as the ratio it is rather than as a rule. "Returns a result
        // type roughly nine times for every throw" is something a reader can
        // check; "never throws" is a claim this cannot support and somebody
        // would have to disprove later.
        if (results + throws > 0)
        {
            yield return new ProjectConvention(
                "Errors",
                results > throws
                    ? $"returns a result type {Ratio(results, throws)} as often as it throws "
                        + $"({results} against {throws})"
                    : $"throws {Ratio(throws, results)} as often as it returns a result type "
                        + $"({throws} against {results})",
                read);
        }

        var share = documented * 100 / read;

        yield return new ProjectConvention(
            "Comments",
            share >= 50
                ? $"{share}% of files carry doc comments, so explanation goes above the member"
                : $"{share}% of files carry doc comments",
            read);
    }

    private static string Ratio(int more, int fewer) =>
        fewer == 0 ? "always" : $"{(double)more / fewer:N1} times";

    private static string? Framework(string manifests) =>
        manifests.Contains("xunit", StringComparison.OrdinalIgnoreCase) ? "xUnit"
        : manifests.Contains("NUnit", StringComparison.OrdinalIgnoreCase) ? "NUnit"
        : manifests.Contains("MSTest", StringComparison.OrdinalIgnoreCase) ? "MSTest"
        : null;

    private static string? Assertions(string manifests) =>
        manifests.Contains("FluentAssertions", StringComparison.OrdinalIgnoreCase)
            ? "FluentAssertions"
            : manifests.Contains("Shouldly", StringComparison.OrdinalIgnoreCase)
                ? "Shouldly"
                : null;

    private static string? Nullable(string manifests) =>
        manifests.Contains("<Nullable>enable", StringComparison.OrdinalIgnoreCase)
            ? "enabled"
            : null;

    private static string? WarningsAsErrors(string manifests) =>
        manifests.Contains("<TreatWarningsAsErrors>true", StringComparison.OrdinalIgnoreCase)
            ? "on, so a warning stops the build"
            : null;

    /// <summary>Records a finding, and says nothing at all when there is none.</summary>
    /// <remarks>
    /// An absent answer is left out rather than written as "none found". A
    /// scaffold listing what it could not detect reads as a list of problems,
    /// and the author then deletes lines instead of writing them.
    /// </remarks>
    private static void Add(List<ProjectConvention> into, string subject, string? finding, int evidence)
    {
        if (finding is { Length: > 0 })
        {
            into.Add(new ProjectConvention(subject, finding, evidence));
        }
    }

    private static string Read(IReadOnlyList<string> files, CancellationToken ct)
    {
        var text = new System.Text.StringBuilder();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                text.AppendLine(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One unreadable manifest costs what it said, not the scan.
            }
        }

        return text.ToString();
    }

    private static IEnumerable<string> Walk(string root, CancellationToken ct)
    {
        var seen = 0;
        var queue = new Queue<string>();

        queue.Enqueue(root);

        while (queue.Count > 0 && seen < MostFiles)
        {
            ct.ThrowIfCancellationRequested();

            var directory = queue.Dequeue();

            string[] files;
            string[] directories;

            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (seen++ >= MostFiles)
                {
                    yield break;
                }

                yield return file;
            }

            foreach (var child in directories)
            {
                if (!Ignored.Contains(Path.GetFileName(child)))
                {
                    queue.Enqueue(child);
                }
            }
        }
    }
}

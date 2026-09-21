using System.Globalization;
using System.Text.RegularExpressions;
using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>One of the documents a run wrote beside its journal.</summary>
/// <param name="Name">The file's own name, which is also how it is asked for.</param>
/// <param name="Kind">brief, report, final report, policy, question or answer.</param>
/// <param name="Subject">Whatever the name says the document is about, unparsed.</param>
/// <param name="Bytes">How big it is, so a page can say so before opening it.</param>
/// <param name="Written">When it was last written.</param>
public sealed record RunDocument(
    string Name,
    string Kind,
    string Subject,
    long Bytes,
    DateTimeOffset Written)
{
    /// <summary>
    /// Whether this document is about that node.
    /// </summary>
    /// <remarks>
    /// Loosely, and on purpose. A brief is <c>brief-implementer-1.json</c> on a
    /// node's first attempt and <c>brief-implementer-1-2.json</c> on its second,
    /// and node names contain hyphens themselves, so the name cannot be split
    /// into node and attempt without knowing the node — which is exactly what
    /// the caller has and the file name does not.
    /// </remarks>
    public bool Mentions(string node) =>
        !string.IsNullOrWhiteSpace(node)
        && (string.Equals(Subject, node, StringComparison.OrdinalIgnoreCase)
            || Subject.StartsWith(node + "-", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The papers a run leaves behind: what each node was told to do, and what it
/// said it did.
/// </summary>
/// <remarks>
/// <para>
/// The journal says what happened. These say what was asked and what came
/// back, in the words the model actually saw — which is the only place to look
/// when a node did something reasonable for a brief nobody meant to give it.
/// </para>
/// <para>
/// Reading is by name against a pattern rather than by listing the directory
/// and matching, so a name carrying a separator or a <c>..</c> is refused
/// before it is ever joined to a path. The directory listing is for showing;
/// the pattern is the thing that decides.
/// </para>
/// </remarks>
public static class RunDocuments
{
    /// <summary>The prefixes a run writes, and what to call each one.</summary>
    /// <remarks>
    /// Ordered longest first so <c>final-report-</c> is not read as a report
    /// about a node called "final".
    /// </remarks>
    private static readonly (string Prefix, string Kind)[] Known =
    [
        ("policy-", "policy"),
        ("answer-", "answer"),
        ("brief-", "brief"),
        ("report-", "report"),
        ("ask-", "question"),
    ];

    /// <summary>The run's own summing-up, which belongs to no node.</summary>
    public const string Final = "final-report.json";

    /// <summary>
    /// Whether a name is one of the documents a run writes, and safe to join.
    /// </summary>
    public static bool Showable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 200
            || name.IndexOfAny(['/', '\\', ':']) >= 0
            || name.Contains("..", StringComparison.Ordinal)
            || !name.EndsWith(".json", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(name, Final, StringComparison.Ordinal)
            || Known.Any(one => name.StartsWith(one.Prefix, StringComparison.Ordinal)
                && name.Length > one.Prefix.Length + ".json".Length);
    }

    /// <summary>What kind of document a name says it is, and who it is about.</summary>
    public static (string Kind, string Subject) Describe(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (string.Equals(name, Final, StringComparison.Ordinal))
        {
            return ("final report", "the run");
        }

        var stem = name.EndsWith(".json", StringComparison.Ordinal)
            ? name[..^".json".Length]
            : name;

        foreach (var (prefix, kind) in Known)
        {
            if (stem.StartsWith(prefix, StringComparison.Ordinal))
            {
                return (kind, stem[prefix.Length..]);
            }
        }

        return ("document", stem);
    }

    /// <summary>How far up the list each kind belongs.</summary>
    /// <remarks>
    /// What a node was told to do and what it said back are what somebody came
    /// for; the questions it stopped to ask are already on the page above, and
    /// the answers are one word each.
    /// </remarks>
    private static int Rank(string kind) => kind switch
    {
        "final report" => 0,
        "brief" => 1,
        "report" => 2,
        "policy" => 3,
        "question" => 4,
        _ => 5,
    };

    /// <summary>Everything showable in a run's directory.</summary>
    /// <remarks>
    /// The run's own summing-up first, then by who each document is about, so
    /// a node's brief and its report sit next to each other rather than in two
    /// blocks with everything else in between. Sorting by file name puts every
    /// answer at the top, which is the one thing nobody opens.
    /// </remarks>
    public static IReadOnlyList<RunDocument> In(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var found = new List<RunDocument>();

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var name = Path.GetFileName(path);

            if (!Showable(name))
            {
                continue;
            }

            var (kind, subject) = Describe(name);
            var about = new FileInfo(path);

            found.Add(new RunDocument(name, kind, subject, about.Length, about.LastWriteTimeUtc));
        }

        return
        [
            .. found
                .OrderBy(one => one.Kind == "final report" ? 0 : 1)
                .ThenBy(one => one.Subject, StringComparer.OrdinalIgnoreCase)
                .ThenBy(one => Rank(one.Kind))
                .ThenBy(one => one.Name, StringComparer.Ordinal),
        ];
    }

    /// <summary>The biggest document that will be handed to a page.</summary>
    /// <remarks>
    /// A report carrying a whole file's contents is a report that would arrive
    /// as several megabytes of JSON into a browser tab that has one line of
    /// room for it. Truncating is said out loud rather than done quietly.
    /// </remarks>
    public const int Most = 256 * 1024;

    /// <summary>
    /// One document, as text, with anything that looks like a credential
    /// replaced.
    /// </summary>
    /// <remarks>
    /// Redacted on the way out rather than trusted to be clean. A brief carries
    /// whatever the goal said and a report carries whatever a node chose to
    /// quote, and neither was written by anybody thinking about who would read
    /// it later over a LAN.
    /// </remarks>
    /// <remarks>
    /// A document the redactor cannot get through is refused rather than sent.
    /// Its patterns are given a second each and a long unbroken run of
    /// characters - a base64 blob a node pasted into its report, say - can use
    /// that up: measured here, a quarter of a megabyte of ordinary JSON takes
    /// about fifty milliseconds and a quarter of a megabyte with no separators
    /// in it at all times out. Unchecked is the one thing it must not be, so
    /// the choice is between showing nothing and showing something nobody
    /// looked at, and it shows nothing.
    /// </remarks>
    /// <param name="directory">The run's own directory.</param>
    /// <param name="name">The document, by the name a run gave it.</param>
    /// <param name="redact">
    /// What checks the text, for a test that needs to make that step fail
    /// without waiting for a regular expression to run out of time.
    /// </param>
    public static OperationResult<string> Read(
        string directory,
        string name,
        Func<string?, string>? redact = null)
    {
        redact ??= SecretRedactor.Redact;

        if (!Showable(name))
        {
            return OperationResult<string>.Fail(
                "That is not one of the documents a run writes.", ExitCode.InvalidArguments);
        }

        var path = Path.Combine(directory, name);

        if (!File.Exists(path))
        {
            return OperationResult<string>.Fail($"There is no {name} in this run.", ExitCode.ProjectNotFound);
        }

        try
        {
            var about = new FileInfo(path);

            if (about.Length > Most)
            {
                using var stream = File.OpenRead(path);
                using var reader = new StreamReader(stream);

                var head = new char[Most];
                var got = reader.ReadBlock(head, 0, Most);

                return OperationResult<string>.Ok(
                    redact(new string(head, 0, got))
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"\n\n[cut here: this document is {about.Length:N0} bytes, and the first {Most:N0} are above]"));
            }

            return OperationResult<string>.Ok(redact(File.ReadAllText(path)));
        }
        catch (RegexMatchTimeoutException)
        {
            return OperationResult<string>.Fail(
                $"{name} could not be checked for credentials in a reasonable time, "
                + "so it is not being shown. Open it from the run's directory.",
                ExitCode.PolicyViolation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string>.Fail($"{name} could not be read: {ex.Message}", ExitCode.GeneralFailure);
        }
    }
}

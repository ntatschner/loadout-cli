using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Loadout.Core.Git;
using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>One file out of a run's outbox, as bytes.</summary>
/// <param name="Bytes">What is in it.</param>
/// <param name="Name">Its own name, for a download.</param>
/// <param name="FromCommit">
/// For a file the commit removed, the commit it was read from instead: the one
/// before. Null for anything read where it was asked for.
/// </param>
public sealed record OutboxRead(byte[] Bytes, string Name, string? FromCommit = null);

/// <summary>One file out of a run's outbox, as a page is shown it.</summary>
/// <param name="Status">200, or why not: 415 for a file that is neither text nor an image, 422 for text nothing could check.</param>
/// <param name="ContentType">What it is served as.</param>
/// <param name="Body">What is served: the text redacted and cut, or the image as it is.</param>
/// <param name="Text">Whether it was read as text, and so redacted and counted.</param>
/// <param name="RedactedLines">How many lines had something taken out.</param>
/// <param name="Truncated">Whether it was cut.</param>
/// <param name="Lines">How many lines the whole file has, for text.</param>
/// <param name="Error">What to say instead, when it is not shown.</param>
public sealed record OutboxView(
    int Status,
    string ContentType,
    byte[] Body,
    bool Text = false,
    int RedactedLines = 0,
    bool Truncated = false,
    int Lines = 0,
    string? Error = null);

/// <summary>Everything an outbox lists, in one archive.</summary>
/// <param name="Bytes">The archive.</param>
/// <param name="Entries">What is in it, by the name each has there.</param>
/// <param name="LeftOut">What could not be put in, and why.</param>
public sealed record OutboxArchive(byte[] Bytes, IReadOnlyList<string> Entries, IReadOnlyList<string> LeftOut);

/// <summary>
/// Reads what a run delivered: one file at the commit that made it, one a node
/// reported as it is on disk now, or all of them together.
/// </summary>
/// <remarks>
/// <para>
/// Only what the run's own outbox lists, matched exactly before it is joined to
/// anything - the rule <see cref="RunDocuments.Read"/> keeps for the papers.
/// A path and a commit come from the page, which is to say from anybody with
/// the token, and the outbox is the one list of what this run actually made.
/// A name that is not on it is refused whatever it looks like, so there is no
/// shape of name that climbs out of the repository to test for.
/// </para>
/// <para>
/// A file a node reported, rather than committed, is listed where the node said
/// it is - which a node can say is anywhere. So one is read only from inside
/// the run's repository, and not through a link, which could lead anywhere.
/// </para>
/// <para>
/// What is shown is redacted, line by line, the way the papers are; what is
/// downloaded is the file exactly, because it is the thing the run made and a
/// redacted copy is a different file. That is also what lets a file the
/// redactor could not get through still be had: it is not shown, and Download
/// still works.
/// </para>
/// </remarks>
public static partial class RunOutboxFiles
{
    /// <summary>How many lines of text a page is shown before it is cut.</summary>
    public const int MostLines = 2000;

    /// <summary>And how many characters, for a file of a few enormous lines.</summary>
    public const int MostCharacters = 512 * 1024;

    /// <summary>The biggest file read from disk.</summary>
    private const long MostToRead = 50L * 1024 * 1024;

    /// <summary>The images a page may show, by extension, with what each is served as.</summary>
    private static readonly Dictionary<string, string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".ico"] = "image/x-icon",
        [".avif"] = "image/avif",
        [".svg"] = "image/svg+xml",
    };

    /// <summary>A commit as git names one: hex, abbreviated or whole.</summary>
    [GeneratedRegex("^[0-9a-fA-F]{7,64}$")]
    private static partial Regex CommitShape();

    /// <summary>
    /// A committed file as that commit left it, or, for one it removed, as the
    /// commit before left it.
    /// </summary>
    /// <remarks>
    /// Failures carry the reason as an exit code a server turns into a status:
    /// <see cref="ExitCode.InvalidArguments"/> for a malformed ask,
    /// <see cref="ExitCode.PolicyViolation"/> for one the outbox does not list,
    /// <see cref="ExitCode.ProjectNotFound"/> for one that is not there, and
    /// <see cref="ExitCode.RepositoryUnavailable"/> where git could not say.
    /// </remarks>
    public static async Task<OperationResult<OutboxRead>> CommittedAsync(
        IGitManager git,
        Outbox outbox,
        string? commit,
        string? path,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(outbox);

        if (commit is not { Length: > 0 } || path is not { Length: > 0 })
        {
            return OperationResult<OutboxRead>.Fail("Say which commit and which path.", ExitCode.InvalidArguments);
        }

        // Exactly, before anything else: the pair has to be one the outbox
        // lists, so the path never reaches git as anything but a listed name.
        var listed = outbox.Files.LastOrDefault(one =>
            string.Equals(one.Commit, commit, StringComparison.Ordinal)
            && string.Equals(one.Path, path, StringComparison.Ordinal));

        if (listed is null)
        {
            return OperationResult<OutboxRead>.Fail(
                "That is not one of the files this run delivered.", ExitCode.PolicyViolation);
        }

        if (outbox.Repository is not { Length: > 0 } repository)
        {
            return OperationResult<OutboxRead>.Fail(
                "Where this run worked cannot be told, so its files cannot be read.", ExitCode.ProjectNotFound);
        }

        // A node reports its commits in its own words, and the outbox lists
        // whatever git agreed was one. Only a hash goes to git from here.
        if (!CommitShape().IsMatch(commit))
        {
            return OperationResult<OutboxRead>.Fail($"'{commit}' is not a commit.", ExitCode.InvalidArguments);
        }

        return await ReadListedAsync(git, repository, listed, ct).ConfigureAwait(false);
    }

    private static async Task<OperationResult<OutboxRead>> ReadListedAsync(
        IGitManager git,
        string repository,
        OutboxFile listed,
        CancellationToken ct)
    {
        string? from = null;
        var revision = listed.Commit;

        // Removed, so the commit has nothing at that path: what was removed is
        // the file as it stood just before.
        if (string.Equals(listed.Change, "removed", StringComparison.Ordinal))
        {
            revision = listed.Commit + "^";

            var parent = await git.ResolveAsync(repository, revision, ct).ConfigureAwait(false);

            from = parent.Succeeded && parent.Value is { Length: > 0 } sha ? sha.Trim() : revision;
        }

        var read = await git.ReadFileAtAsync(repository, revision, listed.Path, ct).ConfigureAwait(false);

        return read.Succeeded
            ? OperationResult<OutboxRead>.Ok(new OutboxRead(read.Value!, Path.GetFileName(listed.Path), from))
            : OperationResult<OutboxRead>.Fail(read.Error!, read.ExitCode);
    }

    /// <summary>A file a node reported, as it is on disk now.</summary>
    public static OperationResult<OutboxRead> Loose(Outbox outbox, string? path)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (path is not { Length: > 0 })
        {
            return OperationResult<OutboxRead>.Fail("Say which file.", ExitCode.InvalidArguments);
        }

        if (!outbox.Loose.Any(one => string.Equals(one.Path, path, StringComparison.Ordinal)))
        {
            return OperationResult<OutboxRead>.Fail(
                "That is not one of the files this run delivered.", ExitCode.PolicyViolation);
        }

        if (Inside(outbox.Repository, path) is not { } full)
        {
            return OperationResult<OutboxRead>.Fail(
                "That file is not inside the repository the run worked in, so it is not read from here.",
                ExitCode.PolicyViolation);
        }

        try
        {
            var file = new FileInfo(full);

            if (!file.Exists)
            {
                return OperationResult<OutboxRead>.Fail("That file is not there any more.", ExitCode.ProjectNotFound);
            }

            if (file.LinkTarget is not null)
            {
                return OperationResult<OutboxRead>.Fail(
                    "That file is a link, which could lead anywhere, so it is not read from here.",
                    ExitCode.PolicyViolation);
            }

            if (file.Length > MostToRead)
            {
                return OperationResult<OutboxRead>.Fail(
                    $"That file is {file.Length:N0} bytes, which is more than this will read.", ExitCode.InvalidArguments);
            }

            return OperationResult<OutboxRead>.Ok(new OutboxRead(File.ReadAllBytes(full), file.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<OutboxRead>.Fail($"That file could not be read: {ex.Message}", ExitCode.RepositoryUnavailable);
        }
    }

    /// <summary>
    /// The whole path, if it is inside the repository; null otherwise, and
    /// where there is no repository to be inside.
    /// </summary>
    private static string? Inside(string? repository, string path)
    {
        if (repository is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository)) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repository, path));

            // Case as the file system has it: the same folder on Windows is
            // spelt either way, and on Linux it is two folders.
            return full.StartsWith(root, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal)
                ? full
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// A file as a page is shown it: an image as itself, text redacted and cut,
    /// and anything else not at all.
    /// </summary>
    /// <param name="read">The file.</param>
    /// <param name="redact">
    /// What checks a line, for a test that needs that step to fail without
    /// waiting for a regular expression to run out of time.
    /// </param>
    public static OutboxView View(OutboxRead read, Func<string?, string>? redact = null)
    {
        ArgumentNullException.ThrowIfNull(read);

        redact ??= SecretRedactor.Redact;

        var extension = Path.GetExtension(read.Name);

        if (Images.TryGetValue(extension, out var image) && image != "image/svg+xml")
        {
            return new OutboxView(200, image, read.Bytes);
        }

        if (Binary(read.Bytes))
        {
            return new OutboxView(
                415,
                "text/plain; charset=utf-8",
                [],
                Error: "That file is neither text nor an image this can show. Download it instead.");
        }

        var text = Decoded(read.Bytes);

        try
        {
            // An SVG is text and is redacted like any, but whole: cutting one
            // leaves markup no image can be drawn from.
            if (image == "image/svg+xml")
            {
                var (svg, redacted) = Redacted(text.Split('\n'), redact);

                return new OutboxView(200, image, Encoding.UTF8.GetBytes(svg), Text: true, RedactedLines: redacted, Lines: Count(text));
            }

            var lines = text.Split('\n');
            var total = Count(text);
            var kept = new List<string>();
            var characters = 0;

            foreach (var line in lines.Take(Math.Min(total, MostLines)))
            {
                if (characters + line.Length > MostCharacters)
                {
                    break;
                }

                kept.Add(line);
                characters += line.Length + 1;
            }

            var (shown, count) = Redacted(kept, redact);

            // The newline the last kept line had, if it had one, so a file
            // shown whole is shown exactly.
            if (kept.Count < lines.Length)
            {
                shown += "\n";
            }

            return new OutboxView(
                200,
                "text/plain; charset=utf-8",
                Encoding.UTF8.GetBytes(shown),
                Text: true,
                RedactedLines: count,
                Truncated: kept.Count < total,
                Lines: total);
        }
        catch (RegexMatchTimeoutException)
        {
            return new OutboxView(
                422,
                "text/plain; charset=utf-8",
                [],
                Text: true,
                Error: $"{read.Name} could not be checked for credentials in a reasonable time, so it is not "
                    + "being shown. Download it instead.");
        }
    }

    /// <summary>Lines redacted one at a time, joined again, and how many changed.</summary>
    /// <remarks>
    /// One at a time so a line is a line before and after: the redactor's
    /// patterns may cross a newline, and a count of lines taken out means
    /// nothing if the lines themselves moved.
    /// </remarks>
    private static (string Text, int Changed) Redacted(IEnumerable<string> lines, Func<string?, string> redact)
    {
        var changed = 0;
        var said = new List<string>();

        foreach (var line in lines)
        {
            var clean = line.Length == 0 ? line : redact(line);

            if (!string.Equals(clean, line, StringComparison.Ordinal))
            {
                changed++;
            }

            said.Add(clean);
        }

        return (string.Join('\n', said), changed);
    }

    /// <summary>How many lines a text has, not counting the empty one after a final newline.</summary>
    private static int Count(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var newlines = text.Count(one => one == '\n');

        return text[^1] == '\n' ? newlines : newlines + 1;
    }

    /// <summary>Whether bytes are not text, by git's own test: a NUL near the start.</summary>
    private static bool Binary(byte[] bytes) =>
        bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).IndexOf((byte)0) >= 0;

    private static string Decoded(byte[] bytes)
    {
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>
    /// Every file the outbox lists, at the commit that last touched it, and the
    /// reported files still there, in one zip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each path once, as the run left it: a file two commits changed is the
    /// later one's, and a file the run last removed is not delivered and is
    /// not in it. A reported file goes in at its place in the repository,
    /// unless a committed file is already there.
    /// </para>
    /// <para>
    /// The files exactly, unredacted, as Download gives one. Anything that
    /// cannot be read is named in <c>left-out.txt</c> inside it rather than
    /// quietly missing, because a zip that is short a file looks complete.
    /// </para>
    /// </remarks>
    public static async Task<OutboxArchive> ZipAsync(IGitManager git, Outbox outbox, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(outbox);

        var latest = new Dictionary<string, OutboxFile>(StringComparer.Ordinal);

        foreach (var one in outbox.Files)
        {
            latest[one.Path] = one;
        }

        var entries = new List<string>();
        var leftOut = new List<string>();

        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var one in latest.Values)
            {
                if (string.Equals(one.Change, "removed", StringComparison.Ordinal))
                {
                    continue;
                }

                if (outbox.Repository is not { Length: > 0 } repository || !CommitShape().IsMatch(one.Commit))
                {
                    leftOut.Add($"{one.Path}: its commit {one.Commit} could not be read");
                    continue;
                }

                var read = await ReadListedAsync(git, repository, one, ct).ConfigureAwait(false);

                if (read.Failed || Named(one.Path) is not { } name)
                {
                    leftOut.Add($"{one.Path}: {read.Error ?? "not a name a zip can hold"}");
                    continue;
                }

                await AddAsync(zip, name, read.Value!.Bytes, ct).ConfigureAwait(false);
                entries.Add(name);
            }

            foreach (var one in outbox.Loose)
            {
                var name = outbox.Repository is { Length: > 0 } root && Inside(root, one.Path) is { } full
                    ? Named(Path.GetRelativePath(root, full))
                    : null;

                if (name is null || entries.Contains(name, StringComparer.Ordinal))
                {
                    // Outside the repository, or already there as committed.
                    if (name is null)
                    {
                        leftOut.Add($"{one.Path}: not inside the repository the run worked in");
                    }

                    continue;
                }

                var read = Loose(outbox, one.Path);

                if (read.Failed)
                {
                    leftOut.Add($"{one.Path}: {read.Error}");
                    continue;
                }

                await AddAsync(zip, name, read.Value!.Bytes, ct).ConfigureAwait(false);
                entries.Add(name);
            }

            if (leftOut.Count > 0)
            {
                await AddAsync(
                    zip,
                    "left-out.txt",
                    Encoding.UTF8.GetBytes(
                        "These were delivered and could not be put in this zip:\n\n"
                        + string.Join('\n', leftOut.Select(SecretRedactor.Redact)) + "\n"),
                    ct).ConfigureAwait(false);
            }
        }

        return new OutboxArchive(buffer.ToArray(), entries, leftOut);
    }

    /// <summary>A path as a zip entry: forward slashes, nothing that climbs.</summary>
    private static string? Named(string path)
    {
        var name = path.Replace('\\', '/').TrimStart('/');

        return name.Length == 0 || name.Split('/').Any(part => part is "" or "." or "..") ? null : name;
    }

    private static async Task AddAsync(ZipArchive zip, string name, byte[] bytes, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);

        await using var stream = entry.Open();

        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }
}

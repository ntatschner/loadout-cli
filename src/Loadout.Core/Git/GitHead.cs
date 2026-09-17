namespace Loadout.Core.Git;

/// <summary>
/// Reads the commit a repository is at without starting git.
/// </summary>
/// <remarks>
/// <para>
/// Starting git costs fifty milliseconds here and more on a cold machine, and
/// a lookup that runs after every edit pays it every time for one answer that
/// is sitting in a text file. So the file is read: <c>HEAD</c> names a branch
/// or a commit, the branch's file holds the commit, and when the branch has
/// been packed the commit is in <c>packed-refs</c> instead. A linked working
/// tree keeps <c>HEAD</c> in its own directory and shares the rest, which the
/// <c>commondir</c> file says where to find.
/// </para>
/// <para>
/// Null for anything this does not understand — a layout git has changed, a
/// file half-written — and the caller falls back to asking git. Wrong is the
/// one answer this must never give, because a wrong commit keys a cache to a
/// tree it does not describe; null merely costs a process.
/// </para>
/// </remarks>
public static class GitHead
{
    /// <summary>The commit at <c>HEAD</c>, or null when it cannot be read with confidence.</summary>
    /// <param name="repositoryPath">The working tree's root, or a directory inside it.</param>
    public static string? Read(string repositoryPath)
    {
        try
        {
            var gitDirectory = FindGitDirectory(repositoryPath);

            if (gitDirectory is null)
            {
                return null;
            }

            var head = ReadLine(Path.Combine(gitDirectory, "HEAD"));

            if (head is null)
            {
                return null;
            }

            if (!head.StartsWith("ref: ", StringComparison.Ordinal))
            {
                return IsCommit(head) ? head : null;
            }

            var reference = head[5..].Trim();

            if (reference.Length == 0 || reference.Contains("..", StringComparison.Ordinal))
            {
                return null;
            }

            // A linked working tree has its own HEAD but shares the refs.
            var common = CommonDirectory(gitDirectory);

            var loose = ReadLine(Path.Combine(common, reference));

            if (loose is not null)
            {
                return IsCommit(loose) ? loose : null;
            }

            return Packed(Path.Combine(common, "packed-refs"), reference);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The root of the working tree a directory is in, or null outside one.
    /// </summary>
    /// <remarks>
    /// The tree the caller is standing in, which for a linked working tree is
    /// not the registered checkout: a project resolved from a worktree carries
    /// the primary clone's path, and an index built against that describes a
    /// different branch from the one being edited. Read from the files, so it
    /// costs a directory walk rather than a process.
    /// </remarks>
    public static string? WorkingTreeRoot(string path)
    {
        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));

            while (directory is not null)
            {
                var dotGit = Path.Combine(directory.FullName, ".git");

                if (Directory.Exists(dotGit) || File.Exists(dotGit))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The <c>.git</c> directory for a working tree, which is either a
    /// directory or a file pointing at one.
    /// </summary>
    private static string? FindGitDirectory(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));

        while (directory is not null)
        {
            var dotGit = Path.Combine(directory.FullName, ".git");

            if (Directory.Exists(dotGit))
            {
                return dotGit;
            }

            if (File.Exists(dotGit))
            {
                var pointer = ReadLine(dotGit);

                if (pointer is null || !pointer.StartsWith("gitdir: ", StringComparison.Ordinal))
                {
                    return null;
                }

                var target = pointer[8..].Trim();

                var resolved = Path.IsPathRooted(target)
                    ? target
                    : Path.GetFullPath(Path.Combine(directory.FullName, target));

                return Directory.Exists(resolved) ? resolved : null;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Where the refs live: the directory itself, unless it says otherwise.</summary>
    private static string CommonDirectory(string gitDirectory)
    {
        var pointer = ReadLine(Path.Combine(gitDirectory, "commondir"));

        if (pointer is null)
        {
            return gitDirectory;
        }

        return Path.IsPathRooted(pointer)
            ? pointer
            : Path.GetFullPath(Path.Combine(gitDirectory, pointer));
    }

    private static string? Packed(string path, string reference)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] is '#' or '^')
            {
                continue;
            }

            var space = line.IndexOf(' ');

            if (space > 0 && line.AsSpan(space + 1).Trim().SequenceEqual(reference))
            {
                var commit = line[..space];

                return IsCommit(commit) ? commit : null;
            }
        }

        return null;
    }

    private static string? ReadLine(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var reader = new StreamReader(path);

        var line = reader.ReadLine()?.Trim();

        return line is { Length: > 0 } ? line : null;
    }

    /// <summary>A SHA-1 or SHA-256 object name, and nothing that merely looks like one.</summary>
    private static bool IsCommit(string text) =>
        text.Length is 40 or 64 && text.All(Uri.IsHexDigit);
}

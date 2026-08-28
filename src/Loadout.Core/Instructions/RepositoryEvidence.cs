using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>What a repository looks like, as far as specialist selection cares.</summary>
/// <param name="Paths">
/// Repository-relative paths, forward-slashed, for glob matching. Bounded: this
/// is evidence, and the hundred-thousandth file says nothing the first thousand
/// did not.
/// </param>
/// <param name="Extensions">How many files carry each extension, lowercased with the dot.</param>
/// <param name="Dependencies">
/// Declared dependency names read out of manifests. A much stronger signal than
/// a file extension, because somebody chose it deliberately.
/// </param>
/// <param name="Truncated">Whether the scan stopped early, so callers can say so.</param>
public sealed record RepositoryEvidence(
    IReadOnlyList<string> Paths,
    IReadOnlyDictionary<string, int> Extensions,
    IReadOnlyList<string> Dependencies,
    bool Truncated)
{
    public static readonly RepositoryEvidence None =
        new([], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), [], false);

    /// <summary>How many files carry an extension, which is what makes a language plausible.</summary>
    public int Count(string extension) =>
        Extensions.TryGetValue(extension, out var count) ? count : 0;
}

/// <summary>Looks at a repository to see what it is made of.</summary>
public interface IRepositoryEvidenceReader
{
    Task<OperationResult<RepositoryEvidence>> ReadAsync(
        string repositoryPath,
        CancellationToken ct = default);
}

/// <summary>
/// Reads the shape of a repository cheaply enough to do it on every launch.
/// </summary>
/// <remarks>
/// <para>
/// Bounded on purpose. A launch already has work to do, and the difference
/// between counting a thousand files and counting two hundred thousand changes
/// nothing about whether a project is written in C#. The scan stops at a file
/// cap, skips the directories that hold other people's code, and never opens
/// anything but a short list of known manifests.
/// </para>
/// <para>
/// Nothing here reads source. Extensions come from names and dependencies come
/// from manifests, so no repository content reaches the launcher, the logs or
/// an agent through this path.
/// </para>
/// </remarks>
internal sealed class RepositoryEvidenceReader : IRepositoryEvidenceReader
{
    /// <summary>How many files to look at before deciding that is enough.</summary>
    private const int MostFiles = 4000;

    /// <summary>Largest manifest to open. A lock file can be megabytes; a manifest is not.</summary>
    private const long LargestManifest = 512 * 1024;

    /// <summary>
    /// Directories that hold code nobody here wrote, or build output.
    /// </summary>
    /// <remarks>
    /// Skipping these is not only about speed. A <c>node_modules</c> full of
    /// TypeScript in a Python project would make the repository look like a
    /// TypeScript project, which is exactly the wrong answer.
    /// </remarks>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", "node_modules", "bin", "obj", "dist", "build", "out",
        "target", "vendor", ".venv", "venv", "__pycache__", ".tox", ".gradle", ".idea",
        ".vs", ".vscode", "packages", ".next", ".nuxt", ".terraform", "coverage",
    };

    /// <summary>
    /// Files worth opening, and how to pull dependency names out of each.
    /// </summary>
    /// <remarks>
    /// Names rather than parsed structure. A specialist declares dependency
    /// evidence as a substring such as <c>Npgsql</c>, and matching that against
    /// the manifest's text finds it whether it was written as a package
    /// reference, a property or an import — without this having to understand
    /// eight packaging formats correctly.
    /// </remarks>
    private static readonly string[] Manifests =
    [
        "package.json", "requirements.txt", "pyproject.toml", "Pipfile", "setup.py",
        "go.mod", "Cargo.toml", "pom.xml", "build.gradle", "build.gradle.kts",
        "Gemfile", "composer.json", "Dockerfile", "docker-compose.yml",
        "docker-compose.yaml", "Directory.Packages.props", "paket.dependencies",
    ];

    /// <summary>Extensions whose files are themselves manifests, such as project files.</summary>
    private static readonly string[] ManifestExtensions = [".csproj", ".fsproj", ".vbproj"];

    /// <inheritdoc />
    public async Task<OperationResult<RepositoryEvidence>> ReadAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            // Not an error: a project may simply not be on this machine yet.
            return OperationResult<RepositoryEvidence>.Ok(RepositoryEvidence.None);
        }

        var paths = new List<string>();
        var extensions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var manifestFiles = new List<string>();
        var truncated = false;

        try
        {
            var root = Path.GetFullPath(repositoryPath);

            truncated = Walk(root, root, paths, extensions, manifestFiles, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<RepositoryEvidence>.Fail(
                $"Could not read '{repositoryPath}': {ex.Message}");
        }

        var dependencies = await ReadManifestsAsync(manifestFiles, ct).ConfigureAwait(false);

        // Sorted so the same repository always yields the same evidence, which
        // is what makes the resolution reproducible and the tests meaningful.
        paths.Sort(StringComparer.Ordinal);

        return OperationResult<RepositoryEvidence>.Ok(new RepositoryEvidence(
            paths,
            extensions,
            dependencies,
            truncated));
    }

    /// <summary>Walks the tree, returning whether it had to stop early.</summary>
    private static bool Walk(
        string root,
        string directory,
        List<string> paths,
        Dictionary<string, int> extensions,
        List<string> manifests,
        CancellationToken ct)
    {
        IEnumerable<string> entries;

        try
        {
            entries = Directory.EnumerateFileSystemEntries(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // One unreadable directory is not a reason to give up on the rest.
            return false;
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(entry);

            if (Directory.Exists(entry))
            {
                if (Ignored.Contains(name))
                {
                    continue;
                }

                if (Walk(root, entry, paths, extensions, manifests, ct))
                {
                    return true;
                }

                continue;
            }

            if (paths.Count >= MostFiles)
            {
                return true;
            }

            var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');

            paths.Add(relative);

            var extension = Path.GetExtension(name);

            if (extension is { Length: > 0 })
            {
                extensions[extension] = extensions.GetValueOrDefault(extension) + 1;
            }

            if (Manifests.Contains(name, StringComparer.OrdinalIgnoreCase)
                || ManifestExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                manifests.Add(entry);
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the manifests as text and returns their lines.
    /// </summary>
    /// <remarks>
    /// Deliberately not parsed. Specialists declare dependency evidence as a
    /// substring, so the text of the manifest is the right thing to match
    /// against; understanding eight packaging formats correctly would be a
    /// large amount of code to arrive at the same answer.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> ReadManifestsAsync(
        IReadOnlyList<string> manifests,
        CancellationToken ct)
    {
        var lines = new List<string>();

        foreach (var manifest in manifests)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (new FileInfo(manifest).Length > LargestManifest)
                {
                    continue;
                }

                var text = await File.ReadAllTextAsync(manifest, ct).ConfigureAwait(false);

                lines.AddRange(text.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A manifest that cannot be read is one signal missing, not a
                // failure: the extension evidence still stands.
            }
        }

        return lines;
    }
}

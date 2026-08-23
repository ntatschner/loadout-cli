using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Instructions;

/// <summary>
/// Brings memory recorded by an agent's own tooling into the workspace.
/// <para>
/// Several repositories were being managed this way before the launcher
/// existed, and their accumulated facts sit in a machine-local directory the
/// launcher does not read. Left alone, adopting the launcher would mean
/// starting from nothing on the projects that had done the most work, which is
/// the wrong incentive and would quietly lose material somebody curated.
/// </para>
/// </summary>
public interface IMemoryImporter
{
    /// <summary>
    /// Where an agent keeps memory for a repository on this machine, or null
    /// when there is none to find.
    /// </summary>
    string? Discover(string repositoryPath);

    /// <summary>
    /// Copies topics into the workspace. Reports what it would do unless
    /// <paramref name="apply"/> is set.
    /// </summary>
    Task<OperationResult<MemoryImport>> ImportAsync(
        string workspaceRoot,
        string slug,
        string sourceDirectory,
        bool apply,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class MemoryImporter : IMemoryImporter
{
    private readonly IEnvironmentProvider _environment;
    private readonly IMemoryService _memory;

    public MemoryImporter(IEnvironmentProvider environment, IMemoryService memory)
    {
        _environment = environment;
        _memory = memory;
    }

    /// <inheritdoc />
    public string? Discover(string repositoryPath)
    {
        var directory = Path.Combine(
            ClaudeHome(),
            "projects",
            DerivedSlug(repositoryPath),
            "memory");

        return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.md").Any()
            ? directory
            : null;
    }

    /// <summary>
    /// The agent's own configuration directory, honouring the override it reads
    /// so a machine that has moved it is still found.
    /// </summary>
    private string ClaudeHome() =>
        _environment.GetVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(_environment.HomeDirectory, ".claude");

    /// <summary>
    /// Reproduces how the agent names a project directory from a repository
    /// path: separators, colons and dots all become hyphens.
    /// <para>
    /// Derived rather than configured because it has to match exactly what the
    /// other tool already wrote, and that is a fact about the other tool.
    /// </para>
    /// </summary>
    internal static string DerivedSlug(string repositoryPath)
    {
        var trimmed = repositoryPath.TrimEnd('/', '\\');

        return new string(trimmed
            .Select(c => c is ':' or '/' or '\\' or '.' ? '-' : c)
            .ToArray());
    }

    /// <inheritdoc />
    public async Task<OperationResult<MemoryImport>> ImportAsync(
        string workspaceRoot,
        string slug,
        string sourceDirectory,
        bool apply,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return OperationResult<MemoryImport>.Fail(
                $"There is no memory at '{sourceDirectory}'.", ExitCode.InvalidArguments);
        }

        var destination = Path.Combine(workspaceRoot, "projects", slug, "memory");

        var imported = new List<MemoryTopic>();
        var skipped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*.md").Order())
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileNameWithoutExtension(file);

            // The index is rebuilt from what actually arrives, so bringing the
            // old one across would only import a list of files that may not
            // all have come with it.
            if (name.Equals("MEMORY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parsed = await MemoryService.ParseAsync(file, ct).ConfigureAwait(false);

            if (parsed.Failed)
            {
                skipped[name] = parsed.Error!;
                continue;
            }

            var topic = parsed.Value!;

            if (topic.Facts.Count == 0)
            {
                skipped[name] = "holds no facts";
                continue;
            }

            var patterns = topic.Facts
                .SelectMany(SecretScanner.Match)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (patterns.Count > 0)
            {
                // Named, never quoted, and never written. The workspace is a
                // Git repository: importing this would commit the credential
                // and publish it on the next push, which no later audit can
                // undo.
                skipped[name] =
                    $"contains something shaped like a credential ({string.Join(", ", patterns)})";

                continue;
            }

            if (File.Exists(Path.Combine(destination, name + ".md")))
            {
                skipped[name] = "already in the workspace";
                continue;
            }

            if (apply)
            {
                try
                {
                    Directory.CreateDirectory(destination);
                    File.Copy(file, Path.Combine(destination, name + ".md"), overwrite: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped[name] = ex.Message;
                    continue;
                }
            }

            imported.Add(topic);
        }

        if (apply && imported.Count > 0)
        {
            var rebuilt = await _memory.RebuildIndexAsync(workspaceRoot, slug, ct)
                .ConfigureAwait(false);

            if (rebuilt.Failed)
            {
                return OperationResult<MemoryImport>.Fail(rebuilt.Error!, rebuilt.ExitCode);
            }
        }

        return OperationResult<MemoryImport>.Ok(
            new MemoryImport(sourceDirectory, imported, skipped, apply));
    }
}

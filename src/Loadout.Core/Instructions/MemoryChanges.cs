using Loadout.Core.Backups;
using Loadout.Models;
using Loadout.Models.Backups;
using Loadout.Models.Instructions;
using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>What a change to memory did, and the backup that undoes it.</summary>
/// <param name="Value">What the change reports.</param>
/// <param name="Backup">The backup taken first, or null when there was nothing to change.</param>
public sealed record MemoryChange<T>(T Value, BackupSet? Backup);

/// <summary>What deleting a topic would do, or did.</summary>
/// <param name="Topic">The topic, as it stood.</param>
/// <param name="LinkedFrom">Topics that link to it with <c>[[its-name]]</c>, which are left as they are.</param>
public sealed record MemoryDeletion(MemoryTopic Topic, IReadOnlyList<string> LinkedFrom);

/// <summary>
/// The changes to memory that are hard to see and hard to undo, each made only
/// after a backup of everything it touches has been taken.
/// </summary>
/// <remarks>
/// <para>
/// In Core rather than in each command, so the command line and the page that
/// will offer the same actions cannot differ about whether a backup is taken.
/// The sequence used to be written out in each command, and compress, the one
/// that rewrites both the instruction file and topics, had none.
/// </para>
/// <para>
/// Only for deletes and bulk changes. A single topic edit is small, and the
/// workspace's own history already covers it.
/// </para>
/// </remarks>
public interface IMemoryChanges
{
    /// <summary>Imports a store of memory into a project's, after backing up the topics it writes.</summary>
    Task<OperationResult<MemoryChange<MemoryImport>>> ImportAsync(
        string workspaceRoot,
        string slug,
        string sourceDirectory,
        CancellationToken ct = default);

    /// <summary>Removes what can be removed without judgement, after backing up what it touches.</summary>
    Task<OperationResult<MemoryChange<MemoryCleanup>>> CleanAsync(
        string workspaceRoot,
        string slug,
        CancellationToken ct = default);

    /// <summary>
    /// Moves durable facts from an instruction file into memory, after backing
    /// up the file, every topic it writes or adds to, and the index.
    /// </summary>
    Task<OperationResult<MemoryChange<CompressionPlan>>> CompressAsync(
        string sourcePath,
        string workspaceRoot,
        string slug,
        int minimumFactsPerTopic = MemoryCompressor.MinimumFactsPerTopic,
        CancellationToken ct = default);

    /// <summary>What deleting a topic would do: the topic, and what links to it. Changes nothing.</summary>
    Task<OperationResult<MemoryDeletion>> PreviewDeleteAsync(
        string workspaceRoot,
        string slug,
        string name,
        CancellationToken ct = default);

    /// <summary>Deletes a project topic, after backing it and the index up.</summary>
    Task<OperationResult<MemoryChange<MemoryDeletion>>> DeleteAsync(
        string workspaceRoot,
        string slug,
        string name,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class MemoryChanges : IMemoryChanges
{
    private readonly IMemoryService _memory;
    private readonly IMemoryImporter _importer;
    private readonly MemoryCompressor _compressor;
    private readonly IBackupService _backups;

    public MemoryChanges(
        IMemoryService memory,
        IMemoryImporter importer,
        MemoryCompressor compressor,
        IBackupService backups)
    {
        _memory = memory;
        _importer = importer;
        _compressor = compressor;
        _backups = backups;
    }

    private static string MemoryDirectory(string workspaceRoot, string slug) =>
        Path.Combine(workspaceRoot, "projects", slug, "memory");

    private static string TopicPath(string workspaceRoot, string slug, string name) =>
        Path.Combine(MemoryDirectory(workspaceRoot, slug), name + ".md");

    private static string IndexPath(string workspaceRoot, string slug) =>
        Path.Combine(MemoryDirectory(workspaceRoot, slug), "MEMORY.md");

    /// <inheritdoc />
    public async Task<OperationResult<MemoryChange<MemoryImport>>> ImportAsync(
        string workspaceRoot,
        string slug,
        string sourceDirectory,
        CancellationToken ct = default)
    {
        var preview = await _importer.ImportAsync(workspaceRoot, slug, sourceDirectory, apply: false, ct)
            .ConfigureAwait(false);

        if (preview.Failed)
        {
            return OperationResult<MemoryChange<MemoryImport>>.Fail(preview.Error!, preview.ExitCode);
        }

        if (preview.Value!.Imported.Count == 0)
        {
            return OperationResult<MemoryChange<MemoryImport>>.Ok(new(preview.Value, null));
        }

        var backup = await BackUpAsync(
            "memory import",
            slug,
            [
                .. preview.Value.Imported.Select(topic => TopicPath(workspaceRoot, slug, topic.Name)),
                IndexPath(workspaceRoot, slug),
            ],
            "import",
            ct).ConfigureAwait(false);

        if (backup.Failed)
        {
            return OperationResult<MemoryChange<MemoryImport>>.Fail(backup.Error!, backup.ExitCode);
        }

        var applied = await _importer.ImportAsync(workspaceRoot, slug, sourceDirectory, apply: true, ct)
            .ConfigureAwait(false);

        return applied.Succeeded
            ? OperationResult<MemoryChange<MemoryImport>>.Ok(new(applied.Value!, backup.Value))
            : OperationResult<MemoryChange<MemoryImport>>.Fail(applied.Error!, applied.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<MemoryChange<MemoryCleanup>>> CleanAsync(
        string workspaceRoot,
        string slug,
        CancellationToken ct = default)
    {
        var backup = await BackUpAsync(
            "memory clean", slug, _memory.CleanupPaths(workspaceRoot, slug), "cleanup", ct).ConfigureAwait(false);

        if (backup.Failed)
        {
            return OperationResult<MemoryChange<MemoryCleanup>>.Fail(backup.Error!, backup.ExitCode);
        }

        var cleaned = await _memory.CleanAsync(workspaceRoot, slug, apply: true, ct).ConfigureAwait(false);

        return cleaned.Succeeded
            ? OperationResult<MemoryChange<MemoryCleanup>>.Ok(new(cleaned.Value!, backup.Value))
            : OperationResult<MemoryChange<MemoryCleanup>>.Fail(cleaned.Error!, cleaned.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<MemoryChange<CompressionPlan>>> CompressAsync(
        string sourcePath,
        string workspaceRoot,
        string slug,
        int minimumFactsPerTopic = MemoryCompressor.MinimumFactsPerTopic,
        CancellationToken ct = default)
    {
        var plan = await _compressor.PlanAsync(sourcePath, workspaceRoot, slug, minimumFactsPerTopic, ct)
            .ConfigureAwait(false);

        if (plan.Failed)
        {
            return OperationResult<MemoryChange<CompressionPlan>>.Fail(plan.Error!, plan.ExitCode);
        }

        if (plan.Value!.Topics.Count == 0)
        {
            return OperationResult<MemoryChange<CompressionPlan>>.Ok(new(plan.Value, null));
        }

        // The instruction file as well as the topics: compress shortens it,
        // and a restore that put the topics back but not the facts they came
        // from would lose them.
        var backup = await BackUpAsync(
            "memory compress",
            slug,
            [
                sourcePath,
                .. plan.Value.Topics.Select(topic => TopicPath(workspaceRoot, slug, topic.Name)),
                IndexPath(workspaceRoot, slug),
            ],
            "compress",
            ct).ConfigureAwait(false);

        if (backup.Failed)
        {
            return OperationResult<MemoryChange<CompressionPlan>>.Fail(backup.Error!, backup.ExitCode);
        }

        var applied = await _compressor.ApplyAsync(sourcePath, workspaceRoot, slug, minimumFactsPerTopic, ct)
            .ConfigureAwait(false);

        return applied.Succeeded
            ? OperationResult<MemoryChange<CompressionPlan>>.Ok(new(applied.Value!, backup.Value))
            : OperationResult<MemoryChange<CompressionPlan>>.Fail(applied.Error!, applied.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<MemoryDeletion>> PreviewDeleteAsync(
        string workspaceRoot,
        string slug,
        string name,
        CancellationToken ct = default)
    {
        var listed = await _memory.ListAsync(workspaceRoot, slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<MemoryDeletion>.Fail(listed.Error!, listed.ExitCode);
        }

        var project = listed.Value!.Where(topic => topic.Scope == MemoryScope.Project).ToList();
        var wanted = MemoryService.Slugify(name);

        if (project.FirstOrDefault(topic => string.Equals(topic.Name, wanted, StringComparison.OrdinalIgnoreCase))
            is not { } topic)
        {
            return OperationResult<MemoryDeletion>.Fail(
                $"There is no topic '{wanted}' in {slug}'s memory. 'loadout memory list {slug}' shows them.",
                ExitCode.InvalidArguments);
        }

        var linkedFrom = project
            .Where(other => other.Name != topic.Name
                && other.Links.Contains(topic.Name, StringComparer.OrdinalIgnoreCase))
            .Select(other => other.Name)
            .ToList();

        return OperationResult<MemoryDeletion>.Ok(new MemoryDeletion(topic, linkedFrom));
    }

    /// <inheritdoc />
    public async Task<OperationResult<MemoryChange<MemoryDeletion>>> DeleteAsync(
        string workspaceRoot,
        string slug,
        string name,
        CancellationToken ct = default)
    {
        var preview = await PreviewDeleteAsync(workspaceRoot, slug, name, ct).ConfigureAwait(false);

        if (preview.Failed)
        {
            return OperationResult<MemoryChange<MemoryDeletion>>.Fail(preview.Error!, preview.ExitCode);
        }

        var topic = preview.Value!.Topic;

        var backup = await BackUpAsync(
            "memory delete", $"{slug}/{topic.Name}", [topic.Path, IndexPath(workspaceRoot, slug)], "delete", ct)
            .ConfigureAwait(false);

        if (backup.Failed)
        {
            return OperationResult<MemoryChange<MemoryDeletion>>.Fail(backup.Error!, backup.ExitCode);
        }

        var deleted = await _memory.DeleteAsync(workspaceRoot, slug, topic.Name, MemoryScope.Project, ct)
            .ConfigureAwait(false);

        return deleted.Succeeded
            ? OperationResult<MemoryChange<MemoryDeletion>>.Ok(new(preview.Value, backup.Value))
            : OperationResult<MemoryChange<MemoryDeletion>>.Fail(deleted.Error!, deleted.ExitCode);
    }

    /// <summary>Takes the backup, or says why the change was not started.</summary>
    private async Task<OperationResult<BackupSet>> BackUpAsync(
        string operation,
        string detail,
        IReadOnlyList<string> paths,
        string what,
        CancellationToken ct)
    {
        var captured = await _backups.CaptureAsync(operation, detail, paths, ct).ConfigureAwait(false);

        return captured.Succeeded
            ? captured
            : OperationResult<BackupSet>.Fail(
                $"The {what} was not started because a backup could not be taken: {captured.Error}",
                captured.ExitCode);
    }
}

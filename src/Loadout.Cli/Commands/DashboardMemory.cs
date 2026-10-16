using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Teams.Daemon;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Tui;
using Spectre.Console;

namespace Loadout.Cli.Commands;

/// <summary>
/// Studio's memory page: read through Core, saved by typing
/// <c>memory replace</c>.
/// </summary>
/// <remarks>
/// <para>
/// A save is checked first through the same function the command calls, as a
/// dry run, because what comes back from running a command is only its exit
/// code. The page would otherwise be told "refused" and have to guess at why,
/// which is the guess the classic page's actions have got wrong before. The
/// command then makes every check again for itself.
/// </para>
/// <para>
/// What the project's agent recorded outside the workspace is shown as the
/// import command would bring it in, as a dry run, and never written from
/// here.
/// </para>
/// </remarks>
internal sealed class DashboardMemory : IStudioMemory
{
    private readonly ICommandCatalogue _commands;
    private readonly IMemoryService _memory;
    private readonly IMemoryImporter _importer;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly TimeProvider _time;
    private readonly CommandOutput _output;

    public DashboardMemory(
        ICommandCatalogue commands,
        IMemoryService memory,
        IMemoryImporter importer,
        IProjectService projects,
        IWorkspaceManager workspace,
        TimeProvider time,
        CommandOutput output)
    {
        _commands = commands;
        _memory = memory;
        _importer = importer;
        _projects = projects;
        _workspace = workspace;
        _time = time;
        _output = output;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StudioProject>> ProjectsAsync(CancellationToken ct)
    {
        var listed = await _projects.ListAsync(ct).ConfigureAwait(false);

        return listed.Succeeded
            ? [.. listed.Value!
                .Select(one => new StudioProject(one.Entry.Slug, one.Entry.Name is { Length: > 0 } name ? name : one.Entry.Slug))
                .OrderBy(one => one.Name, StringComparer.OrdinalIgnoreCase)]
            : [];
    }

    /// <inheritdoc />
    public async Task<OperationResult<object>> TopicsAsync(string slug, CancellationToken ct)
    {
        var project = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);

        if (project.Failed)
        {
            return OperationResult<object>.Fail(project.Error!, project.ExitCode);
        }

        var listed = await _memory.ListAsync(_workspace.LocalPath, project.Value!.Entry.Slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<object>.Fail(listed.Error!, listed.ExitCode);
        }

        return OperationResult<object>.Ok(new
        {
            project = project.Value.Entry.Slug,
            topics = listed.Value!.Select(topic => new
            {
                name = topic.Name,
                scope = topic.Scope.ToString().ToLowerInvariant(),
                description = topic.Description,
                kind = topic.Kind.ToString().ToLowerInvariant(),
                facts = topic.Facts.Count,
                bytes = topic.Bytes,
                written = topic.WrittenUtc,
                editable = topic.Scope != MemoryScope.Machine,
            }),
            agent = await AgentAsync(project.Value.Entry.Slug, project.Value.LocalPath, ct).ConfigureAwait(false),
        });
    }

    /// <summary>What an import would bring in from the agent's own store, or null where there is none.</summary>
    private async Task<object?> AgentAsync(string slug, string? repository, CancellationToken ct)
    {
        if (repository is null || _importer.Discover(repository) is not { } source)
        {
            return null;
        }

        var preview = await _importer.ImportAsync(_workspace.LocalPath, slug, source, apply: false, ct).ConfigureAwait(false);

        return preview.Succeeded
            ? new
            {
                source,
                wouldImport = preview.Value!.Imported.Select(topic => topic.Name),
                skipped = preview.Value.Skipped,
                drifted = preview.Value.Drifted,
                command = $"loadout memory import {slug}",
            }
            : new { source, error = preview.Error };
    }

    /// <inheritdoc />
    public async Task<OperationResult<MemoryTopicText>> ReadAsync(string slug, MemoryScope scope, string topic, CancellationToken ct)
    {
        var project = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);

        return project.Failed
            ? OperationResult<MemoryTopicText>.Fail(project.Error!, project.ExitCode)
            : await _memory.ReadTextAsync(_workspace.LocalPath, project.Value!.Entry.Slug, topic, scope, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<MemoryReplacement>> SaveAsync(
        string slug, MemoryScope scope, string topic, string text, string version, CancellationToken ct)
    {
        var project = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);

        if (project.Failed)
        {
            return OperationResult<MemoryReplacement>.Fail(project.Error!, project.ExitCode);
        }

        var resolved = project.Value!.Entry.Slug;

        var checkedFirst = await _memory
            .ReplaceTextAsync(_workspace.LocalPath, resolved, topic, scope, text, version, dryRun: true, ct)
            .ConfigureAwait(false);

        if (checkedFirst.Failed || checkedFirst.Value!.Conflicted)
        {
            return checkedFirst;
        }

        var file = Path.Combine(Path.GetTempPath(), "loadout-studio-" + Guid.NewGuid().ToString("N") + ".md");

        try
        {
            await File.WriteAllTextAsync(file, text, ct).ConfigureAwait(false);

            _output.WriteLine(
                $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] from Studio: "
                + $"memory replace {Markup.Escape(topic)} --project {Markup.Escape(resolved)}");

            var exit = await _commands.RunAsync(
                "memory replace",
                [
                    topic,
                    DashboardActions.Joined("--project", resolved),
                    DashboardActions.Joined("--scope", scope.ToString().ToLowerInvariant()),
                    DashboardActions.Joined("--file", file),
                    DashboardActions.Joined("--expect", version),
                    "--non-interactive",
                ],
                ct).ConfigureAwait(false);

            // Read back rather than trusted: the topic as it now stands is
            // what the page should go on editing, whichever way this went.
            var now = await _memory.ReadTextAsync(_workspace.LocalPath, resolved, topic, scope, ct).ConfigureAwait(false);

            return exit switch
            {
                0 when now.Succeeded => OperationResult<MemoryReplacement>.Ok(new MemoryReplacement(now.Value!, Conflicted: false)),
                (int)ExitCode.ChangedSinceRead when now.Succeeded =>
                    OperationResult<MemoryReplacement>.Ok(new MemoryReplacement(now.Value!, Conflicted: true)),
                _ => OperationResult<MemoryReplacement>.Fail(
                    $"'loadout memory replace' refused the save (exit code {exit}); the daemon's log has its words.",
                    (ExitCode)exit),
            };
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A temporary copy of something already in the workspace.
            }
        }
    }
}

using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Offers the guidance that looks like it belongs to everybody.
/// </summary>
/// <remarks>
/// Read-only, and it decides nothing. "Publish deliberately" becomes "publish
/// never" if nobody is ever prompted, so this exists to ask — and the reason is
/// printed with every candidate so it can be dismissed in a second.
/// </remarks>
[Description("Show project guidance that never mentions the project, and may belong to everybody.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "share candidates team worthy promote guidance global")]
public sealed class ShareCandidatesCommand : AsyncCommand<ShareCandidatesCommand.Settings>
{
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public ShareCandidatesCommand(
        IWorkspaceManager workspace,
        IProjectService projects,
        IAnsiConsole console)
    {
        _workspace = workspace;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--project <SLUG>")]
        [Description("Project to look at. Defaults to the repository you are in.")]
        public string? Project { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (!_workspace.IsAvailable())
        {
            return output.Fail(
                "There is no workspace on this machine, so there is nothing to share from.",
                ExitCode.WorkspaceSyncFailed);
        }

        var resolution = settings.Project is { Length: > 0 } handle
            ? await _projects.ResolveAsync(handle, cancellationToken).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), cancellationToken)
                .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var project = resolution.Value!;
        var root = Path.Combine(_workspace.LocalPath, "projects", project.Entry.Slug);

        var candidates = ShareCandidates.Find(
            Read(root, _workspace.LocalPath),
            project.Entry.Slug,
            project.Entry.Name);

        if (output.IsJson)
        {
            output.WriteJson(candidates.Select(candidate => new
            {
                path = candidate.RelativePath,
                candidate.Reason,
            }));

            return CommandOutput.Success();
        }

        if (candidates.Count == 0)
        {
            output.WriteLine(
                $"[dim]Nothing under {Markup.Escape(project.Entry.Slug)} looks like it belongs "
                + "to everybody.[/]");

            return CommandOutput.Success();
        }

        output.WriteLine("[bold]Might belong to everybody[/]");

        foreach (var candidate in candidates)
        {
            output.WriteLine($"  {Markup.Escape(candidate.RelativePath)}");
            output.WriteLine($"    [dim]{Markup.Escape(candidate.Reason)}[/]");
        }

        output.WriteBlankLine();
        output.WriteLine(
            "[dim]A guess from what the text says, not a judgement. Move one with "
            + "'loadout share promote <path>', which previews and scans before it writes.[/]");

        return CommandOutput.Success();
    }

    /// <summary>
    /// Every file under a project, as paths relative to the workspace root.
    /// </summary>
    /// <remarks>
    /// The candidate search does its own filtering, and does it on an allow
    /// list. Reading everything here and letting it decide keeps the decision
    /// about what may be shared in one place rather than split across a reader
    /// and a filter that could disagree.
    /// </remarks>
    private static IReadOnlyList<WorkspaceFile> Read(string root, string workspaceRoot)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var files = new List<WorkspaceFile>();

        foreach (var path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            try
            {
                files.Add(new WorkspaceFile(
                    Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/'),
                    File.ReadAllText(path)));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // One unreadable file costs what it said, not the search.
            }
        }

        return files;
    }
}

/// <summary>
/// Moves one piece of guidance from a project to the workspace's global layer.
/// </summary>
/// <remarks>
/// <para>
/// Previewed and scanned, because a workspace is shared and what goes into its
/// global layer is seen by everybody who clones it. That is a smaller
/// disclosure than publishing the workspace itself, and it is still one that
/// cannot be taken back once somebody has pulled.
/// </para>
/// <para>
/// It writes locally and never pushes. Sending it is a separate, deliberate act
/// — the same rule that stops anything else here reaching the network without
/// being asked.
/// </para>
/// </remarks>
[Description("Move project guidance into the workspace's global layer. Previews unless --apply.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "promote share guidance to global team", Mutates = true)]
public sealed class SharePromoteCommand : AsyncCommand<SharePromoteCommand.Settings>
{
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public SharePromoteCommand(IWorkspaceManager workspace, IAnsiConsole console)
    {
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("The file to move, as 'share candidates' printed it.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--apply")]
        [Description("Actually move it. Without this, nothing is written.")]
        public bool Apply { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (!_workspace.IsAvailable())
        {
            return output.Fail(
                "There is no workspace on this machine, so there is nowhere to move it to.",
                ExitCode.WorkspaceSyncFailed);
        }

        var relative = settings.Path.Replace('\\', '/').Trim();

        if (SharePaths.Rejection(relative) is { } rejected)
        {
            return output.Fail(rejected, ExitCode.InvalidArguments);
        }

        var source = Path.GetFullPath(Path.Combine(_workspace.LocalPath, relative));

        // Checked after combining, not before. A path that looks harmless can
        // still resolve outside the workspace, and what matters is where it
        // lands rather than how it was spelled.
        if (!source.StartsWith(Path.GetFullPath(_workspace.LocalPath), StringComparison.OrdinalIgnoreCase))
        {
            return output.Fail(
                "That path resolves outside the workspace.", ExitCode.InvalidArguments);
        }

        if (!File.Exists(source))
        {
            return output.Fail($"'{relative}' is not in the workspace.", ExitCode.InvalidArguments);
        }

        string text;

        try
        {
            text = await File.ReadAllTextAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return output.Fail(
                $"Could not read '{relative}': {exception.Message}", ExitCode.GeneralFailure);
        }

        // Scanned before it moves, because the global layer is seen by
        // everybody who clones the workspace and a pull cannot be taken back.
        // The rule lives in Core so every way of promoting something meets it.
        if (SharedContent.Refusal(text) is { } refusal)
        {
            return output.Fail(
                $"'{relative}' was not moved: {refusal}", ExitCode.PolicyViolation);
        }

        var destination = Path.Combine(
            _workspace.LocalPath, "global", "specialists", Path.GetFileName(source));

        var apply = settings.Apply && !settings.DryRun;

        if (!apply)
        {
            output.WriteLine($"Would move [bold]{Markup.Escape(relative)}[/]");
            output.WriteLine(
                $"  to [bold]{Markup.Escape(Path.GetRelativePath(_workspace.LocalPath, destination).Replace('\\', '/'))}[/]");
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was written. Add --apply to move it.[/]");

            return CommandOutput.Success();
        }

        if (File.Exists(destination))
        {
            return output.Fail(
                $"'{Path.GetFileName(destination)}' is already in the global layer. "
                + "Nothing was overwritten.",
                ExitCode.InvalidArguments);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return output.Fail(
                $"Could not move '{relative}': {exception.Message}", ExitCode.GeneralFailure);
        }

        output.WriteLine($"[green]+[/] Moved into the workspace's global layer.");
        output.WriteLine(
            "[dim]Locally. Nothing has been sent: 'loadout workspace save' is what shares it, "
            + "and it scans again on the way out.[/]");

        return CommandOutput.Success();
    }
}

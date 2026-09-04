using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Checkpoints;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Shared settings for the checkpoint commands.</summary>
public class CheckpointSettings : GlobalSettings
{
    [CommandOption("--project <SLUG>")]
    [Description("Project the checkpoint belongs to. Defaults to the repository you are in.")]
    public string? Project { get; init; }
}

/// <summary>
/// Marks where a project stands, under a name.
/// </summary>
/// <remarks>
/// Backups, Git, handoffs and the ledger each already hold a piece of this. The
/// only new thing is the record that they belong together, taken at one moment
/// under a name somebody chose — which is what makes returning to a moment a
/// thing you can ask for rather than a thing you reconstruct.
/// </remarks>
[Description("Mark where a project stands, under a name you can return to.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "checkpoint mark save point milestone before refactor", Mutates = true)]
public sealed class CheckpointCreateCommand : AsyncCommand<CheckpointCreateCommand.Settings>
{
    private readonly ICheckpointService _checkpoints;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public CheckpointCreateCommand(
        ICheckpointService checkpoints,
        IProjectService projects,
        IAnsiConsole console)
    {
        _checkpoints = checkpoints;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : CheckpointSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("What to call it. Letters, digits, dots, dashes and underscores.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--because <REASON>")]
        [Description("Why you took it, for whoever reads the list later.")]
        public string? Because { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await CheckpointResolution
            .ResolveAsync(_projects, settings, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        if (settings.DryRun)
        {
            // Preview before mutation, as everything else here does: a dry run
            // changes nothing, and says so rather than reporting the words a
            // real run would.
            output.WriteLine(
                $"Would mark [bold]{Markup.Escape(settings.Name)}[/] "
                + $"for {Markup.Escape(slug)}. Nothing was written.");

            return CommandOutput.Success();
        }

        var created = await _checkpoints
            .CreateAsync(slug, settings.Name, settings.Because, cancellationToken)
            .ConfigureAwait(false);

        if (created.Failed)
        {
            return output.Fail(created);
        }

        var checkpoint = created.Value!;

        output.WriteLine($"[green]+[/] Marked [bold]{Markup.Escape(checkpoint.Name)}[/].");

        if (checkpoint.RepositoryWasDirty)
        {
            // Said now rather than discovered on the way back. A commit taken
            // against a dirty tree does not describe what was on disk.
            output.WriteLine(
                "[yellow]note[/] the tree had uncommitted changes, so the commit recorded "
                + "here does not describe everything that was in it.");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Lists a project's checkpoints.</summary>
[Description("List the checkpoints of a project, newest first.")]
[CommandMeta(CommandCategory.Workspace, Intent = "checkpoints list marks save points")]
public sealed class CheckpointListCommand : AsyncCommand<CheckpointSettings>
{
    private readonly ICheckpointService _checkpoints;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public CheckpointListCommand(
        ICheckpointService checkpoints,
        IProjectService projects,
        IAnsiConsole console)
    {
        _checkpoints = checkpoints;
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        CheckpointSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await CheckpointResolution
            .ResolveAsync(_projects, settings, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        var listed = await _checkpoints.ListAsync(slug, cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        if (output.IsJson)
        {
            output.WriteJson(listed.Value!.Select(c => new
            {
                c.Name,
                c.Description,
                created = c.CreatedUtc,
                commit = c.RepositoryCommit,
                branch = c.RepositoryBranch,
                dirty = c.RepositoryWasDirty,
                handoff = c.HandoffName,
            }));

            return CommandOutput.Success();
        }

        if (listed.Value!.Count == 0)
        {
            output.WriteLine($"[dim]{Markup.Escape(slug)} has no checkpoints.[/]");

            return CommandOutput.Success();
        }

        foreach (var checkpoint in listed.Value!)
        {
            output.WriteLine(
                $"{checkpoint.CreatedUtc:yyyy-MM-dd HH:mm}  "
                + $"[bold]{Markup.Escape(checkpoint.Name),-24}[/] "
                + $"[dim]{Markup.Escape(Describe(checkpoint))}[/]");
        }

        return CommandOutput.Success();
    }

    private static string Describe(Models.Checkpoints.Checkpoint checkpoint)
    {
        var parts = new List<string>();

        if (checkpoint.RepositoryCommit is { Length: > 0 } commit)
        {
            parts.Add(commit[..Math.Min(8, commit.Length)] + (checkpoint.RepositoryWasDirty ? "+" : ""));
        }

        if (checkpoint.HandoffName is { Length: > 0 } handoff)
        {
            parts.Add(handoff);
        }

        if (checkpoint.Description.Length > 0)
        {
            parts.Add(checkpoint.Description);
        }

        return string.Join("  ", parts);
    }
}

/// <summary>Puts a checkpoint's workspace back, and says what to do about the rest.</summary>
[Description("Put a checkpoint's workspace files back. Previews unless --apply is given.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "restore checkpoint go back return to mark", Mutates = true)]
public sealed class CheckpointRestoreCommand : AsyncCommand<CheckpointRestoreCommand.Settings>
{
    private readonly ICheckpointService _checkpoints;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public CheckpointRestoreCommand(
        ICheckpointService checkpoints,
        IProjectService projects,
        IAnsiConsole console)
    {
        _checkpoints = checkpoints;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : CheckpointSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("The checkpoint to return to.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--apply")]
        [Description("Actually write the files back. Without it this only shows what would change.")]
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

        var resolution = await CheckpointResolution
            .ResolveAsync(_projects, settings, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        // --dry-run and the absence of --apply mean the same thing, and both
        // have to: a preview flag that some commands honour and others ignore
        // is worse than not having one.
        var apply = settings.Apply && !settings.DryRun;

        var restored = await _checkpoints
            .RestoreAsync(slug, settings.Name, apply, cancellationToken)
            .ConfigureAwait(false);

        if (restored.Failed)
        {
            return output.Fail(restored);
        }

        var report = restored.Value!;

        output.WriteLine(report.Applied
            ? $"[green]+[/] Put back {report.Files.Count} file(s) from "
                + $"[bold]{Markup.Escape(report.Checkpoint.Name)}[/]."
            : $"Would put back {report.Files.Count} file(s) from "
                + $"[bold]{Markup.Escape(report.Checkpoint.Name)}[/]. Nothing was written.");

        foreach (var file in report.Files)
        {
            output.WriteLine($"  [dim]{Markup.Escape(file)}[/]");
        }

        if (report.RepositoryAdvice is { Length: > 0 } advice)
        {
            output.WriteBlankLine();
            output.WriteLine($"[yellow]note[/] {Markup.Escape(advice)}");
        }

        if (report.Checkpoint.HandoffName is { Length: > 0 } handoff)
        {
            output.WriteLine(
                $"[dim]The handoff current at the time was {Markup.Escape(handoff)}.[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Forgets a checkpoint.</summary>
[Description("Forget a checkpoint. The snapshot it pointed at is left alone.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "remove delete forget checkpoint mark", Mutates = true)]
public sealed class CheckpointRemoveCommand : AsyncCommand<CheckpointRemoveCommand.Settings>
{
    private readonly ICheckpointService _checkpoints;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public CheckpointRemoveCommand(
        ICheckpointService checkpoints,
        IProjectService projects,
        IAnsiConsole console)
    {
        _checkpoints = checkpoints;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : CheckpointSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("The checkpoint to forget.")]
        public string Name { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await CheckpointResolution
            .ResolveAsync(_projects, settings, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would forget [bold]{Markup.Escape(settings.Name)}[/]. Nothing was removed.");

            return CommandOutput.Success();
        }

        var removed = await _checkpoints
            .RemoveAsync(slug, settings.Name, cancellationToken).ConfigureAwait(false);

        if (removed.Failed)
        {
            return output.Fail(removed);
        }

        output.WriteLine($"[green]+[/] Forgot [bold]{Markup.Escape(settings.Name)}[/].");

        return CommandOutput.Success();
    }
}

/// <summary>Working out which project a checkpoint command is about.</summary>
internal static class CheckpointResolution
{
    internal static Task<Loadout.Models.Results.OperationResult<Loadout.Models.Projects.ProjectResolution>> ResolveAsync(
        IProjectService projects,
        CheckpointSettings settings,
        CancellationToken ct) =>
        settings.Project is { Length: > 0 } handle
            ? projects.ResolveAsync(handle, ct)
            : projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), ct);
}

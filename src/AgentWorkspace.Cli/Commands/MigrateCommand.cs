using System.ComponentModel;
using AgentWorkspace.Cli.Infrastructure;
using AgentWorkspace.Core.Policies;
using AgentWorkspace.Core.Projects;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Policies;
using AgentWorkspace.Models.Results;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWorkspace.Cli.Commands;

/// <summary>
/// Moves existing agent configuration into the central workspace
/// (spec sections 27 and 96).
/// <para>
/// Always shows the plan before touching anything, and never deletes a tracked
/// file. Removing something Git is tracking rewrites the repository's contents,
/// which is the user's decision to make in a commit they can review, not a side
/// effect of a migration command.
/// </para>
/// </summary>
[Description("Move existing AI tooling files into the central workspace.")]
public sealed class MigrateCommand : AsyncCommand<MigrateCommand.Settings>
{
    private readonly IMigrationService _migrations;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public MigrateCommand(
        IMigrationService migrations,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        _migrations = migrations;
        _projects = projects;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project to migrate. Defaults to the repository in the current directory.")]
        public string? Project { get; init; }

        [CommandOption("--dry-run")]
        [Description("Show the plan and change nothing.")]
        public bool DryRun { get; init; }

        [CommandOption("--yes")]
        [Description("Apply without asking for confirmation.")]
        public bool Yes { get; init; }

        [CommandOption("--include-ignored")]
        [Description("Also move files Git already ignores. They are left alone by default.")]
        public bool IncludeIgnored { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        var resolved = await ResolveAsync(settings).ConfigureAwait(false);
        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var (repositoryPath, slug) = resolved.Value;

        var planResult = await _migrations
            .PlanAsync(repositoryPath, slug, settings.IncludeIgnored)
            .ConfigureAwait(false);
        if (planResult.Failed)
        {
            return output.Fail(planResult);
        }

        var plan = planResult.Value!;

        if (plan.Steps.Count == 0)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { slug, steps = Array.Empty<object>(), applied = false });
            }
            else
            {
                output.WriteLine("[green]Nothing to migrate.[/] "
                    + "[dim]No AI tooling files are tracked or exposed in this repository.[/]");

                if (!settings.IncludeIgnored)
                {
                    output.WriteLine(
                        "[dim]Files Git already ignores were left alone. Move those too with:[/] "
                        + "--include-ignored");
                }
            }

            return CommandOutput.Success();
        }

        if (output.IsJson && settings.DryRun)
        {
            WritePlanJson(output, plan);
            return CommandOutput.Success();
        }

        RenderPlan(output, plan);

        if (settings.DryRun)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Dry run: nothing was changed.[/]");

            return CommandOutput.Success();
        }

        if (!settings.Yes)
        {
            if (!settings.AllowsPrompting)
            {
                // Silently applying a filesystem migration because nobody could
                // be asked would be the wrong default (spec section 37).
                return output.Fail(
                    "Migration changes files, so it needs confirmation. Pass --yes to proceed, "
                    + "or --dry-run to see the plan.",
                    ExitCode.InvalidArguments);
            }

            if (!_console.Confirm("Perform this migration?", defaultValue: false))
            {
                output.WriteLine("[dim]Cancelled.[/]");
                return CommandOutput.Success();
            }
        }

        var applyResult = await _migrations.ApplyAsync(plan).ConfigureAwait(false);
        if (applyResult.Failed)
        {
            return output.Fail(applyResult);
        }

        var applied = applyResult.Value!;

        if (output.IsJson)
        {
            WritePlanJson(output, applied);
            return CommandOutput.Success();
        }

        output.WriteBlankLine();
        output.WriteLine($"[green]Migrated[/] {applied.Steps.Count} item(s) into the workspace.");

        if (applied.BackupId is not null)
        {
            // Printed on success rather than buried in help. The moment
            // somebody wants this is the moment they realise the migration did
            // something they did not expect, and hunting for the incantation
            // then is the worst possible time.
            output.WriteLine(
                $"[dim]Undo it with:[/] agentctl backup restore {Markup.Escape(applied.BackupId)}");
        }

        WriteInstructionHint(output, applied);

        if (applied.TrackedLeftInPlace.Count > 0)
        {
            // The most important sentence the command prints: the copy happened
            // but the repository is not clean yet, and only a commit can finish
            // the job.
            output.WriteBlankLine();
            output.WriteLine("[yellow]Still tracked in the repository:[/]");

            foreach (var path in applied.TrackedLeftInPlace)
            {
                output.WriteLine($"  {Markup.Escape(path)}");
            }

            output.WriteBlankLine();
            output.WriteLine("[dim]These were copied, not removed. Remove them yourself with:[/]");
            output.WriteLine($"  git rm -r --cached {string.Join(' ', applied.TrackedLeftInPlace)}");
            output.WriteLine("[dim]then commit, so the removal is reviewable.[/]");
        }

        return CommandOutput.Success();
    }

    private async Task<OperationResult<(string RepositoryPath, string Slug)>> ResolveAsync(
        Settings settings)
    {
        var directory = settings.Repo ?? Directory.GetCurrentDirectory();

        var resolution = settings.Project is not null
            ? await _projects.ResolveAsync(settings.Project).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(directory).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return OperationResult<(string, string)>.Fail(resolution.Error!, resolution.ExitCode);
        }

        var project = resolution.Value!;

        return project.LocalPath is null
            ? OperationResult<(string, string)>.Fail(
                $"'{project.Entry.Name}' is not present on this machine.",
                ExitCode.RepositoryUnavailable)
            : OperationResult<(string, string)>.Ok((project.LocalPath, project.Entry.Slug));
    }

    private static void RenderPlan(CommandOutput output, MigrationPlan plan)
    {
        output.WriteLine("[bold]Migration plan[/]");

        foreach (var step in plan.Steps)
        {
            output.WriteBlankLine();
            output.WriteLine(Markup.Escape(step.RepositoryRelativePath));

            var label = step.Kind switch
            {
                PolicyFindingKind.Tracked => "[yellow]tracked[/]",
                PolicyFindingKind.UntrackedAndVisible => "[dim]untracked[/]",
                _ => "[dim]ignored[/]",
            };

            output.WriteLine($"  {label}");
            output.WriteLine($"  -> {Markup.Escape(step.WorkspaceRelativePath)}");

            if (step.Kind == PolicyFindingKind.Tracked)
            {
                output.WriteLine("  [dim]will be copied, not removed[/]");
            }
        }
    }

    /// <summary>
    /// Points out an instruction file that will now load on every session.
    /// <para>
    /// Said here because this is the moment it becomes true. A CLAUDE.md that
    /// was a file in a repository is, after this command, part of what every
    /// launch pays for, and nobody would think to go looking for a budget
    /// command they have not heard of.
    /// </para>
    /// </summary>
    private void WriteInstructionHint(CommandOutput output, MigrationPlan plan)
    {
        const long WorthMentioning = 8 * 1024;

        // Measured at the destination, not the source. An untracked file has
        // already been removed from the repository by this point, so measuring
        // where it came from would find nothing and the hint would never fire
        // for the case it exists to catch.
        var instructions = plan.Steps
            .Where(step => !step.IsDirectory
                && step.WorkspaceRelativePath.EndsWith("instructions.md", StringComparison.Ordinal))
            .Select(step => Path.Combine(
                _workspace.LocalPath,
                step.WorkspaceRelativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .Select(path => new FileInfo(path).Length)
            .DefaultIfEmpty(0)
            .Max();

        if (instructions < WorthMentioning)
        {
            return;
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[yellow]{instructions / 1024}KB of instructions now load on every session[/] "
            + "[dim]for this project, whatever the task. See what that costs, and what could be "
            + "scoped to the paths it actually concerns:[/]");

        output.WriteLine($"  agentctl rules budget {Markup.Escape(plan.Slug)}");
        output.WriteLine($"  agentctl rules split {Markup.Escape(plan.Slug)} --write-map");
    }

    private static void WritePlanJson(CommandOutput output, MigrationPlan plan) =>
        output.WriteJson(new
        {
            slug = plan.Slug,
            applied = plan.Applied,
            steps = plan.Steps.Select(s => new
            {
                source = s.RepositoryRelativePath,
                destination = s.WorkspaceRelativePath,
                kind = s.Kind.ToString(),
                isDirectory = s.IsDirectory,
            }),
            trackedLeftInPlace = plan.TrackedLeftInPlace,
            backupId = plan.BackupId,
        });
}

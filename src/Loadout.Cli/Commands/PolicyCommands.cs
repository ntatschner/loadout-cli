using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Policies;
using Loadout.Models.Results;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Reports whether a repository holds agent tooling files
/// (spec sections 49 and 97).
/// <para>
/// This is the check that makes the launcher's central claim verifiable. A
/// tracked agent file is a violation; one that is present but untracked is a
/// single <c>git add .</c> from becoming one, and is worth saying so.
/// </para>
/// </summary>
[Description("Check a repository for tracked AI tooling files.")]
[CommandMeta(CommandCategory.Safety, Intent = "check repository tracked ai tooling files")]
public sealed class RepoCheckCommand : AsyncCommand<RepoCheckCommand.Settings>
{
    private readonly IPolicyService _policies;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public RepoCheckCommand(
        IPolicyService policies,
        IProjectService projects,
        IAnsiConsole console)
    {
        _policies = policies;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project to check. Defaults to the repository in the current directory.")]
        public string? Project { get; init; }

        [CommandOption("--all")]
        [Description("Check every project available on this machine.")]
        public bool All { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var pathsResult = await ResolveTargetsAsync(settings).ConfigureAwait(false);
        if (pathsResult.Failed)
        {
            return output.Fail(pathsResult);
        }

        var reports = new List<PolicyReport>();

        foreach (var path in pathsResult.Value!)
        {
            var result = await _policies.CheckAsync(path).ConfigureAwait(false);

            if (result.Failed)
            {
                return output.Fail(result);
            }

            reports.Add(result.Value!);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                repositories = reports.Select(r => new
                {
                    path = r.RepositoryPath,
                    verdict = r.Verdict,
                    compliant = r.IsCompliant,
                    globalExcludes = r.HasGlobalExcludes,
                    preCommitHook = r.HasPreCommitHook,
                    findings = r.Findings.Select(f => new
                    {
                        path = f.Path,
                        kind = f.Kind.ToString(),
                        pattern = f.Pattern,
                    }),
                }),
            });
        }
        else
        {
            foreach (var report in reports)
            {
                Render(output, report, reports.Count > 1);
            }
        }

        // A violation is a policy failure, which has its own exit code so
        // automation can gate a merge on it (spec section 40).
        return reports.Any(r => !r.IsCompliant)
            ? (int)ExitCode.PolicyViolation
            : CommandOutput.Success();
    }

    private async Task<OperationResult<IReadOnlyList<string>>> ResolveTargetsAsync(Settings settings)
    {
        if (settings.All)
        {
            var list = await _projects.ListAsync().ConfigureAwait(false);

            if (list.Failed)
            {
                return OperationResult<IReadOnlyList<string>>.Fail(list.Error!, list.ExitCode);
            }

            // Projects that are registered but not cloned here have nothing to
            // check, and skipping them quietly is right: their absence is not a
            // policy problem.
            return OperationResult<IReadOnlyList<string>>.Ok(
                list.Value!.Where(p => p.IsAvailableLocally).Select(p => p.LocalPath!).ToList());
        }

        if (settings.Project is not null)
        {
            var resolved = await _projects.ResolveAsync(settings.Project).ConfigureAwait(false);

            if (resolved.Failed)
            {
                return OperationResult<IReadOnlyList<string>>.Fail(resolved.Error!, resolved.ExitCode);
            }

            if (resolved.Value!.LocalPath is null)
            {
                return OperationResult<IReadOnlyList<string>>.Fail(
                    $"'{resolved.Value.Entry.Name}' is not present on this machine.",
                    ExitCode.RepositoryUnavailable);
            }

            return OperationResult<IReadOnlyList<string>>.Ok([resolved.Value.LocalPath]);
        }

        return OperationResult<IReadOnlyList<string>>.Ok(
            [settings.Repo ?? Directory.GetCurrentDirectory()]);
    }

    private static void Render(CommandOutput output, PolicyReport report, bool showPath)
    {
        if (showPath)
        {
            output.WriteBlankLine();
            output.WriteLine($"[bold]{Markup.Escape(report.RepositoryPath)}[/]");
        }
        else
        {
            output.WriteLine("[bold]AI repository separation[/]");
        }

        output.WriteBlankLine();

        foreach (var violation in report.Violations)
        {
            output.WriteLine(
                $"[red]x[/] tracked  {Markup.Escape(violation.Path)}  "
                + $"[dim]matches {Markup.Escape(violation.Pattern)}[/]");
        }

        foreach (var warning in report.Warnings)
        {
            output.WriteLine(
                $"[yellow]![/] untracked and not ignored  {Markup.Escape(warning.Path)}");
        }

        if (report.Violations.Count == 0)
        {
            output.WriteLine("[green]+[/] No agent tooling files are tracked");
        }

        output.WriteLine(report.HasGlobalExcludes
            ? "[green]+[/] Global Git excludes configured"
            : "[yellow]![/] No global Git excludes  [dim]run: loadout protect --global[/]");

        output.WriteLine(report.HasPreCommitHook
            ? "[green]+[/] Pre-commit protection installed"
            : "[yellow]![/] No pre-commit protection  [dim]run: loadout protect[/]");

        var colour = report.Verdict switch
        {
            "COMPLIANT" => "green",
            "WARNING" => "yellow",
            _ => "red",
        };

        output.WriteBlankLine();
        output.WriteLine($"Repository separation: [{colour}]{report.Verdict}[/]");
    }
}

/// <summary>
/// Installs the Git-level protections of spec sections 50 and 51.
/// </summary>
[Description("Install Git protections that keep AI tooling files out of a repository.")]
[CommandMeta(CommandCategory.Safety, Intent = "hook excludes guard prevent commit agent files", Mutates = true, Example = "--global")]
public sealed class ProtectCommand : AsyncCommand<ProtectCommand.Settings>
{
    private readonly IPolicyService _policies;
    private readonly IProjectService _projects;
    private readonly Loadout.Core.Workspace.IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public ProtectCommand(
        IPolicyService policies,
        IProjectService projects,
        Loadout.Core.Workspace.IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        _policies = policies;
        _projects = projects;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project to protect. Defaults to the repository in the current directory.")]
        public string? Project { get; init; }

        [CommandOption("--all")]
        [Description("Protect every project available on this machine.")]
        public bool All { get; init; }

        [CommandOption("--global")]
        [Description("Configure the global Git exclude file instead of a repository hook.")]
        public bool Global { get; init; }

        [CommandOption("--remove")]
        [Description("Remove a hook the launcher installed.")]
        public bool Remove { get; init; }

        [CommandOption("--refresh-hook")]
        [Description(
            "Instead of the Git hook, install Claude's after-edit hook that keeps the symbol "
            + "index current, in the project's own Claude settings.")]
        public bool RefreshHook { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        if (settings.RefreshHook)
        {
            return await RefreshHookAsync(output, settings, cancellationToken).ConfigureAwait(false);
        }

        if (settings.Global)
        {
            if (settings.DryRun)
            {
                output.WriteLine(
                    "[bold]Would configure[/] core.excludesFile to the launcher's global "
                    + "exclude file. Nothing was changed.");

                return CommandOutput.Success();
            }

            var result = await _policies.InstallGlobalExcludesAsync().ConfigureAwait(false);

            if (result.Failed)
            {
                return output.Fail(result);
            }

            output.WriteLine(
                $"[green]Configured[/] core.excludesFile [dim]{Markup.Escape(result.Value!)}[/]");

            return CommandOutput.Success();
        }

        var targets = await ResolveTargetsAsync(settings).ConfigureAwait(false);
        if (targets.Failed)
        {
            return output.Fail(targets);
        }

        if (settings.DryRun)
        {
            var verb = settings.Remove ? "remove" : "install";

            foreach (var path in targets.Value!)
            {
                output.WriteLine(
                    $"[bold]Would {verb}[/] the pre-commit hook in {Markup.Escape(path)}");
            }

            output.WriteLine("[dim]Nothing was changed.[/]");

            return CommandOutput.Success();
        }

        foreach (var path in targets.Value!)
        {
            var result = settings.Remove
                ? await _policies.RemoveHookAsync(path).ConfigureAwait(false)
                : await _policies.InstallHookAsync(path).ConfigureAwait(false);

            if (result.Failed)
            {
                // A refusal here is usually "somebody else's hook is already
                // there", which must not stop the remaining repositories.
                output.WriteLine($"[yellow]skipped[/] {Markup.Escape(path)}  "
                    + $"[dim]{Loadout.Tui.Shown.Safely(result.Error!)}[/]");

                continue;
            }

            output.WriteLine(settings.Remove
                ? $"[green]Removed[/] hook from {Markup.Escape(path)}"
                : $"[green]Protected[/] {Markup.Escape(path)}");
        }

        return CommandOutput.Success();
    }

    private async Task<OperationResult<IReadOnlyList<string>>> ResolveTargetsAsync(Settings settings)
    {
        if (settings.All)
        {
            var list = await _projects.ListAsync().ConfigureAwait(false);

            return list.Failed
                ? OperationResult<IReadOnlyList<string>>.Fail(list.Error!, list.ExitCode)
                : OperationResult<IReadOnlyList<string>>.Ok(
                    list.Value!.Where(p => p.IsAvailableLocally).Select(p => p.LocalPath!).ToList());
        }

        if (settings.Project is not null)
        {
            var resolved = await _projects.ResolveAsync(settings.Project).ConfigureAwait(false);

            if (resolved.Failed)
            {
                return OperationResult<IReadOnlyList<string>>.Fail(resolved.Error!, resolved.ExitCode);
            }

            return resolved.Value!.LocalPath is null
                ? OperationResult<IReadOnlyList<string>>.Fail(
                    $"'{resolved.Value.Entry.Name}' is not present on this machine.",
                    ExitCode.RepositoryUnavailable)
                : OperationResult<IReadOnlyList<string>>.Ok([resolved.Value.LocalPath]);
        }

        return OperationResult<IReadOnlyList<string>>.Ok(
            [settings.Repo ?? Directory.GetCurrentDirectory()]);
    }

    /// <summary>
    /// Installs or removes the after-edit hook in each project's Claude settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project's own settings file in the workspace, which the Claude
    /// adapter already passes with <c>--settings</c>, rather than the user's
    /// file: the hook refreshes one project's index, and a hook that fires in
    /// every session on the machine for a project it cannot work out is a
    /// hook that mostly says nothing at a cost.
    /// </para>
    /// <para>
    /// Lives here rather than in a command of its own because this is already
    /// the command that installs hooks and previews before it does. The file
    /// it writes is in the workspace repository, so the change travels with
    /// the next <c>workspace save</c>, which is said on the way out.
    /// </para>
    /// </remarks>
    private async Task<int> RefreshHookAsync(
        CommandOutput output,
        Settings settings,
        CancellationToken cancellationToken)
    {
        if (settings.Global)
        {
            return output.Fail(
                "The after-edit hook is installed per project, not globally: it refreshes one "
                + "project's index. Name a project, use --all, or run it inside one.",
                ExitCode.InvalidArguments);
        }

        if (!_workspace.IsAvailable())
        {
            return output.Fail(
                "There is no workspace on this machine, so there is no project settings file "
                + "to write the hook into.",
                ExitCode.RepositoryUnavailable);
        }

        var launcher = Launcher();

        if (!settings.Remove && launcher is null)
        {
            return output.Fail(
                "This launcher's own path could not be determined, so the hook cannot name it.",
                ExitCode.GeneralFailure);
        }

        var slugs = await SlugsAsync(settings, cancellationToken).ConfigureAwait(false);

        if (slugs.Failed)
        {
            return output.Fail(slugs);
        }

        var verb = settings.Remove ? "remove" : "install";

        foreach (var slug in slugs.Value!)
        {
            var path = Path.Combine(
                _workspace.LocalPath, "projects", slug, "agents", "claude", "settings.json");

            if (settings.DryRun)
            {
                output.WriteLine(
                    $"[bold]Would {verb}[/] the after-edit hook for {Markup.Escape(slug)} in "
                    + $"{Markup.Escape(path)}");

                continue;
            }

            if (settings.Remove)
            {
                var removed = await RefreshHookInstaller.UninstallAsync(path, cancellationToken)
                    .ConfigureAwait(false);

                if (removed.Failed)
                {
                    output.WriteLine(
                        $"[yellow]skipped[/] {Markup.Escape(slug)}  [dim]{Shown.Safely(removed.Error!)}[/]");

                    continue;
                }

                output.WriteLine(removed.Value
                    ? $"[green]Removed[/] the after-edit hook from {Markup.Escape(slug)}"
                    : $"[dim]{Markup.Escape(slug)} had no after-edit hook to remove.[/]");

                continue;
            }

            var installed = await RefreshHookInstaller
                .InstallAsync(path, launcher!.Value.Executable, launcher.Value.EntryAssembly, slug, cancellationToken)
                .ConfigureAwait(false);

            if (installed.Failed)
            {
                output.WriteLine(
                    $"[yellow]skipped[/] {Markup.Escape(slug)}  [dim]{Shown.Safely(installed.Error!)}[/]");

                continue;
            }

            output.WriteLine(installed.Value!.AlreadyThere
                ? $"[dim]{Markup.Escape(slug)} already has the after-edit hook.[/]"
                : $"[green]Installed[/] the after-edit hook for {Markup.Escape(slug)}: "
                    + $"[dim]{Markup.Escape(installed.Value.Command)}[/]");
        }

        if (settings.DryRun)
        {
            output.WriteLine("[dim]Nothing was changed.[/]");
        }
        else
        {
            output.WriteLine(
                "[dim]The settings file is in the workspace, so this travels with the next "
                + "'loadout workspace save'.[/]");
        }

        return CommandOutput.Success();
    }

    /// <summary>
    /// How to start this launcher again from a hook: the process, and the
    /// assembly too when the process is only a host for it.
    /// </summary>
    /// <remarks>
    /// The shipped launcher is one executable and its process path is the
    /// answer. A development build is run as <c>dotnet loadout.dll</c>, whose
    /// process path is <c>dotnet.exe</c>, and a hook naming that alone starts
    /// the host with nothing to run. The first install of this hook did
    /// exactly that, which is why the host case is told apart by name.
    /// </remarks>
    private static (string Executable, string? EntryAssembly)? Launcher()
    {
        if (Environment.ProcessPath is not { Length: > 0 } process)
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(process);

        if (!name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return (process, null);
        }

        // The assembly's own location is empty inside a single-file build, so
        // it is found by name in the application directory instead — and a
        // single-file build never reaches this branch anyway, since its
        // process is the launcher itself.
        var assembly = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;

        if (assembly is not { Length: > 0 })
        {
            return null;
        }

        var entry = Path.Combine(AppContext.BaseDirectory, assembly + ".dll");

        return File.Exists(entry) ? (process, entry) : null;
    }

    /// <summary>The projects the hook applies to, by slug.</summary>
    private async Task<OperationResult<IReadOnlyList<string>>> SlugsAsync(
        Settings settings,
        CancellationToken cancellationToken)
    {
        if (settings.All)
        {
            var list = await _projects.ListAsync(cancellationToken).ConfigureAwait(false);

            return list.Failed
                ? OperationResult<IReadOnlyList<string>>.Fail(list.Error!, list.ExitCode)
                : OperationResult<IReadOnlyList<string>>.Ok(
                    [.. list.Value!.Where(p => p.IsAvailableLocally).Select(p => p.Entry.Slug)]);
        }

        var resolved = settings.Project is not null
            ? await _projects.ResolveAsync(settings.Project, cancellationToken).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), cancellationToken)
                .ConfigureAwait(false);

        return resolved.Failed
            ? OperationResult<IReadOnlyList<string>>.Fail(resolved.Error!, resolved.ExitCode)
            : OperationResult<IReadOnlyList<string>>.Ok([resolved.Value!.Entry.Slug]);
    }
}

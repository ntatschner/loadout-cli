using System.ComponentModel;
using Loadout.Cli.Infrastructure;
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
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
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
    private readonly IAnsiConsole _console;

    public ProtectCommand(IPolicyService policies, IProjectService projects, IAnsiConsole console)
    {
        _policies = policies;
        _projects = projects;
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
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        if (settings.Global)
        {
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
                    + $"[dim]{Markup.Escape(result.Error!)}[/]");

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
}

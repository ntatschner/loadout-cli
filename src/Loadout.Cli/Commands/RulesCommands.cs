using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Shared project resolution for the rule commands.</summary>
public abstract class RulesCommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : RulesCommandBase<TSettings>.RulesSettings
{
    protected RulesCommandBase(
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        Projects = projects;
        Workspace = workspace;
        Console = console;
    }

    protected IProjectService Projects { get; }

    protected IWorkspaceManager Workspace { get; }

    protected IAnsiConsole Console { get; }

    public class RulesSettings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public string? Project { get; init; }
    }

    /// <summary>
    /// The instruction files a launch loads whatever the task, whether or not
    /// they exist: a manifest entry pointing at a missing file is itself worth
    /// reporting, so this cannot filter them out.
    /// <para>
    /// The per-agent instructions file is included even though the manifest does
    /// not list it. The compiler adds it implicitly, and it is where a migrated
    /// CLAUDE.md lands, so leaving it out would report an empty budget for
    /// exactly the projects carrying the largest one.
    /// </para>
    /// </summary>
    protected async Task<IReadOnlyList<string>> CoreInstructionPathsAsync(string slug)
    {
        var manifest = await Workspace.ReadProjectAsync(slug).ConfigureAwait(false);

        if (manifest.Failed)
        {
            return [];
        }

        var root = Workspace.LocalPath;
        var projectRoot = Path.Combine(root, "projects", slug);

        var paths = manifest.Value!.Context.Global
            .Select(relative => Path.Combine(root, ToNative(relative)))
            .Concat(manifest.Value.Context.Project
                .Select(relative => Path.Combine(projectRoot, ToNative(relative))))
            .ToList();

        var agent = manifest.Value.Agents.Default;

        if (!string.IsNullOrWhiteSpace(agent))
        {
            var agentInstructions = Path.Combine(projectRoot, "agents", agent, "instructions.md");

            // Only when it exists. Most projects have none, and reporting its
            // absence as a defect would be noise on every one of them.
            if (File.Exists(agentInstructions))
            {
                paths.Add(agentInstructions);
            }
        }

        return paths;
    }

    private static string ToNative(string relative) =>
        relative.Replace('/', Path.DirectorySeparatorChar);

    protected async Task<OperationResult<string>> ResolveSlugAsync(TSettings settings)
    {
        var resolution = settings.Project is not null
            ? await Projects.ResolveAsync(settings.Project).ConfigureAwait(false)
            : await Projects
                .ResolveFromDirectoryAsync(settings.Repo ?? Directory.GetCurrentDirectory())
                .ConfigureAwait(false);

        return resolution.Failed
            ? OperationResult<string>.Fail(resolution.Error!, resolution.ExitCode)
            : OperationResult<string>.Ok(resolution.Value!.Entry.Slug);
    }
}

/// <summary>Lists the instruction rules that apply to a project.</summary>
[Description("List the path-scoped instruction rules for a project.")]
public sealed class RulesListCommand : RulesCommandBase<RulesListCommand.Settings>
{
    private readonly IRuleService _rules;

    public RulesListCommand(
        IRuleService rules,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
        : base(projects, workspace, console) => _rules = rules;

    public sealed class Settings : RulesSettings
    {
        [CommandOption("--for <PATH>")]
        [Description("Show only the rules that would load for these paths. Repeatable.")]
        public string[] For { get; init; } = [];
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(Console, settings);

        var slug = await ResolveSlugAsync(settings).ConfigureAwait(false);
        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        var loaded = await _rules.LoadAsync(Workspace.LocalPath, slug.Value!).ConfigureAwait(false);
        if (loaded.Failed)
        {
            return output.Fail(loaded);
        }

        var rules = settings.For.Length > 0
            ? _rules.Select(loaded.Value!, settings.For)
            : loaded.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = slug.Value,
                rules = rules.Select(r => new
                {
                    r.Name,
                    r.Description,
                    r.Globs,
                    r.AlwaysApply,
                    r.Bytes,
                    r.Path,
                }),
            });

            return CommandOutput.Success();
        }

        if (rules.Count == 0)
        {
            output.WriteLine(settings.For.Length > 0
                ? "[dim]No rules match those paths.[/]"
                : $"[dim]{Markup.Escape(slug.Value!)} has no rules. "
                  + "Add Markdown files under the project's rules/ directory in the workspace.[/]");

            return CommandOutput.Success();
        }

        var table = new Table().Border(TableBorder.Simple).BorderColor(Color.Grey);
        table.AddColumn("Rule");
        table.AddColumn("Scope");
        table.AddColumn(new TableColumn("Size").RightAligned());
        table.AddColumn("Description");

        foreach (var rule in rules)
        {
            var scope = rule.AlwaysApply
                ? "[yellow]always[/]"
                : rule.Globs.Count > 0
                    ? Markup.Escape(string.Join(", ", rule.Globs))
                    : "[red]unscoped[/]";

            table.AddRow(
                Markup.Escape(rule.Name),
                scope,
                FormatBytes(rule.Bytes),
                Markup.Escape(rule.Description));
        }

        output.Write(table);

        return CommandOutput.Success();
    }

    internal static string FormatBytes(long bytes) => bytes < 1024
        ? bytes.ToString(CultureInfo.InvariantCulture) + "B"
        : (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + "KB";
}

/// <summary>
/// Reports what a session pays for before it has done anything.
/// <para>
/// The number that matters is not how much instruction text exists but how much
/// of it loads regardless of the task. A rule that only applies to migrations
/// is free while nobody is writing one; an always-apply rule is charged on every
/// turn of every session, forever.
/// </para>
/// </summary>
[Description("Report how much instruction text loads on every session.")]
public sealed class RulesBudgetCommand : RulesCommandBase<RulesBudgetCommand.Settings>
{
    /// <summary>
    /// The point past which always-loaded instructions start crowding out the
    /// work. Advisory: it prompts a look, it does not fail anything by itself.
    /// </summary>
    private const long ComfortableAlwaysLoadedBytes = 20 * 1024;

    private readonly IRuleService _rules;

    public RulesBudgetCommand(
        IRuleService rules,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
        : base(projects, workspace, console) => _rules = rules;

    public sealed class Settings : RulesSettings
    {
        [CommandOption("--strict")]
        [Description("Exit non-zero when the always-loaded budget is exceeded or a rule is unscoped.")]
        public bool Strict { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(Console, settings);

        var slug = await ResolveSlugAsync(settings).ConfigureAwait(false);
        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        var loaded = await _rules.LoadAsync(Workspace.LocalPath, slug.Value!).ConfigureAwait(false);
        if (loaded.Failed)
        {
            return output.Fail(loaded);
        }

        var core = (await CoreInstructionPathsAsync(slug.Value!).ConfigureAwait(false))
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);

        var budget = _rules.Budget(loaded.Value!, core);
        var overBudget = budget.AlwaysLoadedBytes > ComfortableAlwaysLoadedBytes;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = slug.Value,
                alwaysLoadedBytes = budget.AlwaysLoadedBytes,
                scopedBytes = budget.ScopedBytes,
                overBudget,
                alwaysApply = budget.AlwaysApplyRules.Select(r => new { r.Name, r.Bytes }),
                scoped = budget.ScopedRules.Select(r => new { r.Name, r.Bytes, r.Globs }),
                unscoped = budget.UnscopedRules.Select(r => new { r.Name, r.Bytes }),
            });

            return Exit(settings, overBudget, budget);
        }

        output.WriteLine($"[bold]{Markup.Escape(slug.Value!)}[/]");
        output.WriteBlankLine();

        var colour = overBudget ? "yellow" : "green";

        output.WriteLine(
            $"  Always loaded  [{colour}]{RulesListCommand.FormatBytes(budget.AlwaysLoadedBytes)}[/]"
            + $"  [dim]{budget.AlwaysApplyRules.Count} rule(s) plus core instructions[/]");

        output.WriteLine(
            $"  On demand      [dim]{RulesListCommand.FormatBytes(budget.ScopedBytes)}"
            + $"  {budget.ScopedRules.Count} scoped rule(s)[/]");

        if (budget.UnscopedRules.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine(
                "[yellow]Unscoped rules[/] [dim]load every session because they declare no globs "
                + "and no alwaysApply. Give each one a scope or mark it deliberate:[/]");

            foreach (var rule in budget.UnscopedRules)
            {
                output.WriteLine(
                    $"  - {Markup.Escape(rule.Name)}  "
                    + $"[dim]{RulesListCommand.FormatBytes(rule.Bytes)}[/]");
            }
        }

        if (overBudget)
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"[yellow]Over the {ComfortableAlwaysLoadedBytes / 1024}KB comfortable budget.[/] "
                + "[dim]Scope the largest rules to the paths they actually concern.[/]");
        }

        return Exit(settings, overBudget, budget);
    }

    private static int Exit(Settings settings, bool overBudget, InstructionBudget budget) =>
        settings.Strict && (overBudget || budget.UnscopedRules.Count > 0)
            ? (int)ExitCode.GeneralFailure
            : CommandOutput.Success();
}

/// <summary>
/// Checks the instruction layer for the defects that quietly cost every session
/// tokens: duplication, contradiction between scope and always-apply, oversize,
/// and imports nobody has counted.
/// </summary>
[Description("Check a project's instruction rules and core context for defects.")]
public sealed class RulesAuditCommand : RulesCommandBase<RulesAuditCommand.Settings>
{
    private readonly IRuleService _rules;

    public RulesAuditCommand(
        IRuleService rules,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
        : base(projects, workspace, console) => _rules = rules;

    public sealed class Settings : RulesSettings
    {
        [CommandOption("--strict")]
        [Description("Exit non-zero on warnings as well as errors, for use in CI.")]
        public bool Strict { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(Console, settings);

        var slug = await ResolveSlugAsync(settings).ConfigureAwait(false);
        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        var loaded = await _rules.LoadAsync(Workspace.LocalPath, slug.Value!).ConfigureAwait(false);
        if (loaded.Failed)
        {
            return output.Fail(loaded);
        }

        var core = await CoreInstructionPathsAsync(slug.Value!).ConfigureAwait(false);
        var audit = RuleAuditor.Audit(loaded.Value!, core, slug.Value!);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = audit.Slug,
                verdict = audit.Verdict,
                rules = audit.Rules.Count,
                alwaysLoadedBytes = audit.Budget.AlwaysLoadedBytes,
                findings = audit.Findings.Select(f => new
                {
                    f.Rule,
                    severity = f.Severity.ToString().ToLowerInvariant(),
                    kind = f.Kind,
                    f.Detail,
                }),
            });
        }
        else
        {
            Render(output, audit);
        }

        if (audit.Errors.Any())
        {
            return (int)ExitCode.GeneralFailure;
        }

        return settings.Strict && audit.Warnings.Any()
            ? (int)ExitCode.GeneralFailure
            : CommandOutput.Success();
    }

    private static void Render(CommandOutput output, RuleAudit audit)
    {
        output.WriteLine(
            $"[bold]{Markup.Escape(audit.Slug)}[/]  [dim]{audit.Rules.Count} rule(s), "
            + $"{RulesListCommand.FormatBytes(audit.Budget.AlwaysLoadedBytes)} loaded every "
            + "session[/]");

        output.WriteBlankLine();

        if (audit.Findings.Count == 0)
        {
            output.WriteLine("[green]HEALTHY[/]  Nothing to report.");
            return;
        }

        foreach (var group in audit.Findings
            .GroupBy(f => f.Severity)
            .OrderByDescending(g => g.Key))
        {
            foreach (var finding in group)
            {
                var colour = finding.Severity switch
                {
                    RuleFindingSeverity.Error => "red",
                    RuleFindingSeverity.Warning => "yellow",
                    _ => "grey",
                };

                var where = finding.Rule is null ? string.Empty : Markup.Escape(finding.Rule) + " ";

                output.WriteLine(
                    $"  [{colour}]{group.Key.ToString().ToLowerInvariant(),-7}[/] "
                    + $"{where}{Markup.Escape(finding.Detail)}");
            }
        }

        output.WriteBlankLine();

        var verdictColour = audit.Verdict switch
        {
            "ACTION REQUIRED" => "red",
            "NEEDS ATTENTION" => "yellow",
            _ => "green",
        };

        output.WriteLine($"[{verdictColour}]{audit.Verdict}[/]");
    }
}

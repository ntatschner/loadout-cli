using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Backups;
using Loadout.Core.Configuration;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Decomposes an oversized instruction file into a small core plus path-scoped
/// rules.
/// <para>
/// The command that makes scoping worth having. Rules only save anything once
/// the instructions are actually split, and doing that by hand on a file that
/// has accumulated for a year is a job people start and abandon.
/// </para>
/// </summary>
[Description("Split an oversized instruction file into path-scoped rules.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "break up oversized instructions scoped", Mutates = true)]
public sealed class RulesSplitCommand : RulesCommandBase<RulesSplitCommand.Settings>
{
    private readonly IBackupService _backups;
    private readonly YamlStore _yaml;
    private readonly InstructionSplitter _splitter = new();

    public RulesSplitCommand(
        IBackupService backups,
        YamlStore yaml,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
        : base(projects, workspace, console)
    {
        _backups = backups;
        _yaml = yaml;
    }

    public sealed class Settings : RulesSettings
    {
        [CommandOption("--from <FILE>")]
        [Description("Instruction file to split. Defaults to the largest always-loaded one.")]
        public string? From { get; init; }

        [CommandOption("--map <FILE>")]
        [Description("The routing map. Defaults to split-map.yaml beside the instruction file.")]
        public string? Map { get; init; }

        [CommandOption("--write-map")]
        [Description("Write a starter map listing every section, then stop.")]
        public bool WriteMap { get; init; }

        [CommandOption("--apply")]
        [Description("Actually split. Without this the command only shows what it would do.")]
        public bool ApplyRequested { get; init; }

        /// <summary>
        /// Whether to go ahead, once --dry-run has had its say.
        /// </summary>
        /// <remarks>
        /// --dry-run is accepted on every command and always means the
        /// same thing, so it overrides --apply rather than
        /// competing with it. Asking for both is not a contradiction to
        /// resolve: the more cautious of the two is what was meant.
        /// </remarks>
        public bool Apply => ApplyRequested && !DryRun;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(Console, settings);

        var slug = await ResolveSlugAsync(settings).ConfigureAwait(false);
        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        var projectRoot = Path.Combine(Workspace.LocalPath, "projects", slug.Value!);

        var source = settings.From
            ?? await LargestCoreFileAsync(slug.Value!).ConfigureAwait(false)
            ?? Path.Combine(projectRoot, "instructions.md");
        var mapPath = settings.Map ?? Path.Combine(Path.GetDirectoryName(source)!, "split-map.yaml");
        var ruleDirectory = Path.Combine(projectRoot, "rules");

        if (settings.WriteMap)
        {
            return await WriteMapAsync(output, source, mapPath).ConfigureAwait(false);
        }

        if (!File.Exists(mapPath))
        {
            // Refused rather than guessed. Deciding which instructions matter
            // for which paths is a judgement about the project, and a tool that
            // guessed would scope things wrongly and silently.
            return output.Fail(
                $"No split map at '{mapPath}'. Write a starter one with:\n"
                + "  loadout rules split --write-map\n"
                + "then set the globs for each rule and run this again.",
                ExitCode.ConfigurationInvalid);
        }

        var map = await _yaml.LoadAsync(mapPath, () => new SplitMap()).ConfigureAwait(false);
        if (map.Failed)
        {
            return output.Fail(map);
        }

        var planned = await _splitter.PlanAsync(source, map.Value!).ConfigureAwait(false);
        if (planned.Failed)
        {
            return output.Fail(planned);
        }

        var plan = planned.Value!;

        if (output.IsJson && !settings.Apply)
        {
            WriteJson(output, plan);
            return CommandOutput.Success();
        }

        Render(output, plan);

        if (!plan.IsLossless)
        {
            return output.Fail(
                $"{plan.MissingLines.Count} line(s) would be lost, so nothing was written.",
                ExitCode.PolicyViolation);
        }

        if (!settings.Apply)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was changed. Add --apply to split.[/]");

            return CommandOutput.Success();
        }

        if (settings.AllowsPrompting
            && !Console.Confirm(
                $"Move {plan.Rules.Count} section group(s) out of "
                + $"'{Markup.Escape(Path.GetFileName(source))}'?",
                defaultValue: false))
        {
            output.WriteLine("[dim]Cancelled.[/]");
            return CommandOutput.Success();
        }

        var captured = await _backups.CaptureAsync(
            "rules split",
            slug.Value!,
            InstructionSplitter.AffectedPaths(plan, ruleDirectory)).ConfigureAwait(false);

        if (captured.Failed)
        {
            return output.Fail(
                "The split was not started because a backup could not be taken: " + captured.Error,
                captured.ExitCode);
        }

        var applied = await _splitter.ApplyAsync(plan, ruleDirectory).ConfigureAwait(false);
        if (applied.Failed)
        {
            return output.Fail(applied);
        }

        var result = applied.Value! with { BackupId = captured.Value!.Id };

        if (output.IsJson)
        {
            WriteJson(output, result);
            return CommandOutput.Success();
        }

        output.WriteBlankLine();
        output.WriteLine($"[green]Split[/] into {result.Rules.Count} scoped rule(s).");
        output.WriteLine(
            $"[dim]Undo it with:[/] loadout backup restore {Markup.Escape(result.BackupId!)}");

        return CommandOutput.Success();
    }

    /// <summary>
    /// The always-loaded instruction file worth splitting, which is the largest
    /// one a launch actually reads.
    /// <para>
    /// Guessing a fixed name would miss the common case. A migrated CLAUDE.md
    /// lands at <c>agents/&lt;agent&gt;/instructions.md</c>, not at the project
    /// root, so a default of "instructions.md" would report the one file people
    /// most want to split as not existing.
    /// </para>
    /// </summary>
    private async Task<string?> LargestCoreFileAsync(string slug)
    {
        var paths = await CoreInstructionPathsAsync(slug).ConfigureAwait(false);

        return paths
            .Where(File.Exists)
            .OrderByDescending(path => new FileInfo(path).Length)
            .FirstOrDefault();
    }

    private async Task<int> WriteMapAsync(CommandOutput output, string source, string mapPath)
    {
        var suggested = await _splitter.SuggestMapAsync(source).ConfigureAwait(false);
        if (suggested.Failed)
        {
            return output.Fail(suggested);
        }

        if (File.Exists(mapPath))
        {
            // Overwriting would discard the routing decisions somebody has
            // already made, which is the expensive part of this exercise.
            return output.Fail(
                $"'{mapPath}' already exists. Delete or move it if you want a fresh one.",
                ExitCode.InvalidArguments);
        }

        var saved = await _yaml
            .SaveAsync(mapPath, suggested.Value!, restrictPermissions: false)
            .ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        var rules = suggested.Value!.Rules;
        var scoped = rules.Count(rule => rule.Globs.Count > 0);

        output.WriteLine($"[green]Wrote[/] {Markup.Escape(mapPath)}");

        // Counted rather than asserted. This said every section came out with
        // no globs, which stopped being true as soon as the suggestion learned
        // to read paths out of a heading: against a real file fifteen of
        // nineteen arrived already scoped, and the advice was to go and fill in
        // what was in front of them.
        output.WriteLine(scoped == 0
            ? $"[dim]It routes each of the {rules.Count} section(s) into a rule of its own, none "
                + "of which it could scope. Set the globs for the ones worth scoping, delete the "
                + "entries for anything that should stay in the core, then run:[/]"
            : $"[dim]It routes {rules.Count} section(s) into a rule each and suggests globs for "
                + $"{scoped} of them, read from what those sections name. Check those, set the "
                + "globs for any others worth scoping, delete the entries for anything that should "
                + "stay in the core, then run:[/]");

        output.WriteLine("  loadout rules split");

        return CommandOutput.Success();
    }

    private static void Render(CommandOutput output, SplitPlan plan)
    {
        output.WriteLine(
            $"[bold]{Markup.Escape(Path.GetFileName(plan.SourcePath))}[/]  "
            + $"[dim]{RulesListCommand.FormatBytes(plan.CoreBytes + plan.MovedBytes)} today[/]");

        output.WriteBlankLine();

        foreach (var rule in plan.Rules)
        {
            var scope = rule.Globs.Count > 0
                ? Markup.Escape(string.Join(", ", rule.Globs))
                : "[red]no globs[/]";

            output.WriteLine(
                $"  [bold]{Markup.Escape(rule.Name)}[/]  {scope}  "
                + $"[dim]{RulesListCommand.FormatBytes(
                    System.Text.Encoding.UTF8.GetByteCount(rule.Body))}[/]");

            foreach (var section in rule.Sections)
            {
                output.WriteLine($"    from  {Markup.Escape(section)}");
            }
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"  Always loaded  {RulesListCommand.FormatBytes(plan.CoreBytes)}  "
            + $"[dim]was {RulesListCommand.FormatBytes(plan.CoreBytes + plan.MovedBytes)}[/]");

        output.WriteLine(
            $"  On demand      [dim]{RulesListCommand.FormatBytes(plan.MovedBytes)}[/]");

        if (plan.IsLossless)
        {
            output.WriteBlankLine();
            output.WriteLine("[green]Every line is accounted for.[/]");

            return;
        }

        output.WriteBlankLine();
        output.WriteLine("[red]These lines have nowhere to go:[/]");

        foreach (var line in plan.MissingLines.Take(15))
        {
            output.WriteLine($"  {Markup.Escape(line)}");
        }

        if (plan.MissingLines.Count > 15)
        {
            output.WriteLine($"  [dim]and {plan.MissingLines.Count - 15} more[/]");
        }
    }

    private static void WriteJson(CommandOutput output, SplitPlan plan) =>
        output.WriteJson(new
        {
            source = plan.SourcePath,
            applied = plan.Applied,
            lossless = plan.IsLossless,
            coreBytes = plan.CoreBytes,
            movedBytes = plan.MovedBytes,
            backupId = plan.BackupId,
            rules = plan.Rules.Select(r => new
            {
                r.Name,
                r.Description,
                r.Globs,
                r.Sections,
                bytes = System.Text.Encoding.UTF8.GetByteCount(r.Body),
            }),
            missingLines = plan.MissingLines,
        });
}

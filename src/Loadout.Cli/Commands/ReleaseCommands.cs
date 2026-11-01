using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Delivery;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>What the release commands are given: the project.</summary>
public class ReleaseProjectSettings : GlobalSettings
{
    [CommandArgument(0, "<PROJECT>")]
    [Description("The project to release, by slug or name.")]
    public string Project { get; init; } = string.Empty;
}

/// <summary>
/// Says what a release of a project would be: the recipe, the version
/// proposed and why, the commits since the last release, notes to start
/// from, and anything that would stop it now.
/// </summary>
/// <remarks>
/// Changes nothing and reaches nothing: what origin holds is as of the last
/// fetch, and the proposal is only ever a starting point for <c>release cut</c>.
/// </remarks>
[Description("Say what a release of a project would be: the version proposed, the notes, and what would stop it.")]
[CommandMeta(CommandCategory.Start, Intent = "release plan version propose notes tag next")]
public sealed class ReleasePlanCommand : AsyncCommand<ReleasePlanCommand.Settings>
{
    private readonly IReleaseService _releases;
    private readonly IAnsiConsole _console;

    public ReleasePlanCommand(IReleaseService releases, IAnsiConsole console)
    {
        _releases = releases;
        _console = console;
    }

    public sealed class Settings : ReleaseProjectSettings
    {
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var plan = await _releases.PlanAsync(settings.Project, cancellationToken).ConfigureAwait(false);

        if (plan.Failed)
        {
            return output.Fail(plan);
        }

        var p = plan.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = p.Project,
                recipe = p.Recipe,
                branch = p.Branch,
                head = p.Head,
                lastTag = p.LastTag,
                versionFile = p.VersionFile,
                fileVersion = p.FileVersion,
                proposed = p.Proposed,
                bump = p.Bump,
                since = p.Since,
                notes = p.Notes,
                problems = p.Problems,
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(p.Project)}[/] releases with [bold]{Markup.Escape(p.Recipe)}[/] from {Markup.Escape(p.Branch)}.");
        output.WriteLine(
            $"Last release: {Markup.Escape(p.LastTag ?? "none")}"
            + (p.VersionFile.Length > 0 ? $"; {Markup.Escape(p.VersionFile)} says {Markup.Escape(p.FileVersion ?? "nothing readable")}" : string.Empty)
            + ".");
        output.WriteLine(p.Proposed is null
            ? $"[dim]No version proposed: {Markup.Escape(p.Bump)}.[/]"
            : $"Proposed: [bold]{Markup.Escape(p.Proposed)}[/] ({Markup.Escape(p.Bump)}).");

        if (p.Since.Count > 0)
        {
            output.WriteLine(string.Empty);
            output.WriteLine($"{p.Since.Count} commit(s) since:");

            foreach (var subject in p.Since.Take(20))
            {
                output.WriteLine($"  {Markup.Escape(subject)}");
            }

            if (p.Since.Count > 20)
            {
                output.WriteLine($"  [dim]and {p.Since.Count - 20} more[/]");
            }
        }

        output.WriteLine(string.Empty);
        output.WriteLine("Notes to start from:");
        output.WriteLine($"[dim]{Markup.Escape(p.Notes)}[/]");

        foreach (var problem in p.Problems)
        {
            output.WriteLine($"[yellow]![/] {Markup.Escape(problem)}");
        }

        return CommandOutput.Success();
    }
}

/// <summary>
/// Releases a project with the version and notes given, the way its manifest says.
/// </summary>
/// <remarks>
/// <para>
/// Nothing proposed is used unless it is given back: the version comes from
/// <c>--version</c> and the notes from <c>--notes</c>, since both are public
/// the moment the tag is pushed.
/// </para>
/// <para>
/// It shows what it will do and asks first, and where nothing can ask it
/// refuses without <c>--yes</c>. <c>--dry-run</c> shows and stops.
/// </para>
/// </remarks>
[Description("Release a project with the version and notes you give: bump, tag and push, or start its workflow.")]
[CommandMeta(CommandCategory.Start, Intent = "release cut tag push version publish ship", Mutates = true)]
public sealed class ReleaseCutCommand : AsyncCommand<ReleaseCutCommand.Settings>
{
    private readonly IReleaseService _releases;
    private readonly IAnsiConsole _console;

    public ReleaseCutCommand(IReleaseService releases, IAnsiConsole console)
    {
        _releases = releases;
        _console = console;
    }

    public sealed class Settings : ReleaseProjectSettings
    {
        [CommandOption("--version <VERSION>")]
        [Description("The version to release, as major.minor.patch. 'release plan' proposes one.")]
        public string? Version { get; init; }

        [CommandOption("--notes <TEXT>")]
        [Description("The release notes, which go in the tag's message. 'release plan' proposes some to start from.")]
        public string? Notes { get; init; }

        [CommandOption("--yes")]
        [Description("Release without asking, having seen what it does. Needed where nothing can ask.")]
        public bool Yes { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var plan = await _releases.PlanAsync(settings.Project, cancellationToken).ConfigureAwait(false);

        if (plan.Failed)
        {
            return output.Fail(plan);
        }

        var p = plan.Value!;

        if (_releases.CannotCut(p, settings.Version, settings.Notes) is { } refused)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
        }

        var version = settings.Version!.Trim();
        var tag = p.TagPrefix + version;
        var does = p.Recipe == ReleaseService.Workflow
            ? $"start {p.Project}'s release workflow on {p.Branch} for {version}"
            : (p.FileVersion == version ? string.Empty : $"bump {p.VersionFile} to {version} and commit it on {p.Branch}, then ")
                + $"tag {tag} with these notes and push {(p.FileVersion == version ? "the tag" : "both")} to origin";

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { dryRun = true, project = p.Project, recipe = p.Recipe, tag, bump = p.FileVersion != version, notes = settings.Notes!.Trim() });

                return CommandOutput.Success();
            }

            output.WriteLine($"[dim]Dry run: nothing was changed.[/] It would {Markup.Escape(does)}.");
            output.WriteLine(Markup.Escape(settings.Notes!.Trim()));

            return CommandOutput.Success();
        }

        if (!settings.Yes)
        {
            if (!settings.AllowsPrompting)
            {
                return output.Fail(
                    $"Releasing {p.Project} pushes to origin, so it needs your say-so. Look with --dry-run, then pass --yes.",
                    ExitCode.InvalidArguments);
            }

            output.WriteLine($"This will {Markup.Escape(does)}:");
            output.WriteLine(Markup.Escape(settings.Notes!.Trim()));

            if (!_console.Confirm($"Release {Markup.Escape(tag)}?", defaultValue: false))
            {
                output.WriteLine("[dim]Not released. Nothing was changed.[/]");

                return CommandOutput.Success();
            }
        }

        var cut = await _releases.CutAsync(p, version, settings.Notes!, cancellationToken).ConfigureAwait(false);

        if (cut.Failed)
        {
            return output.Fail(cut);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { tag = cut.Value!.Tag, commit = cut.Value.Commit, bumped = cut.Value.Bumped, said = cut.Value.Said });

            return CommandOutput.Success();
        }

        output.WriteLine(p.Recipe == ReleaseService.Workflow
            ? $"[green]+[/] {Markup.Escape(p.Project)}'s release workflow is started for {Markup.Escape(version)}."
            : $"[green]+[/] {Markup.Escape(cut.Value!.Tag)} is pushed{(cut.Value.Bumped ? $", after the bump to {Markup.Escape(version)}" : string.Empty)}.");

        return CommandOutput.Success();
    }
}

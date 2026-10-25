using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Voice;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Projects;
using Loadout.Models.Voice;
using Loadout.Platform.Common;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Options shared by the voice commands.</summary>
public class VoiceSettings : GlobalSettings
{
    [CommandOption("--project <PROJECT>")]
    [Description("Project whose voice to use. Defaults to the repository you are in.")]
    public string? Project { get; init; }

    [CommandOption("--guide <GUIDE>")]
    [Description("Which guide, as an id or id/version: prose, which ships, or one you drafted. Defaults to prose.")]
    public string? Guide { get; init; }
}

/// <summary>The guide in use, the project if any, and the profiles in force.</summary>
public sealed record VoiceContext(VoiceGuide Guide, ProjectResolution? Project, IReadOnlyList<VoiceLayer> Layers)
{
    /// <summary>The named style chosen for this work, or null.</summary>
    public string? Chosen { get; init; }
}

/// <summary>
/// Shared by the voice commands: which guide, which project, and the profiles
/// in force for them.
/// </summary>
public abstract class VoiceCommandBase<TSettings> : InstructionsCommandBase<TSettings>
    where TSettings : VoiceSettings
{
    protected VoiceCommandBase(
        VoiceStore store,
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(instructions, workspace, projects, git, console)
    {
        Store = store;
    }

    protected VoiceStore Store { get; }

    /// <summary>The guide a command works on: the one named, or prose.</summary>
    protected virtual string GuideOf(TSettings settings) => settings.Guide ?? VoiceGuides.Prose().Id;

    /// <summary>The guide, the project if any, and the layers in force.</summary>
    protected async Task<Loadout.Models.Results.OperationResult<VoiceContext>> ResolveAsync(
        TSettings settings,
        CancellationToken ct)
    {
        var found = await Store.FindGuideAsync(GuideOf(settings), ct).ConfigureAwait(false);

        if (found.Failed)
        {
            return Loadout.Models.Results.OperationResult<VoiceContext>.Fail(found.Error!, found.ExitCode);
        }

        var guide = found.Value!;

        var project = await ProjectAsync(settings.Project, settings.Repo, ct).ConfigureAwait(false);

        var manifest = project is not null && WorkspacePath is not null
            ? (await Workspace.ReadProjectAsync(project.Entry.Slug, ct).ConfigureAwait(false)).Value
            : null;

        var chosen = StyleChoice.Of(
            manifest,
            settings.Profile,
            await Instructions.DefaultStyleAsync(ct).ConfigureAwait(false));

        var layers = await Store
            .LayersAsync(guide.Id, chosen, project?.Entry.Slug, ct)
            .ConfigureAwait(false);

        return layers.Failed
            ? Loadout.Models.Results.OperationResult<VoiceContext>.Fail(layers.Error!, layers.ExitCode)
            : Loadout.Models.Results.OperationResult<VoiceContext>.Ok(
                new VoiceContext(guide, project, layers.Value!) { Chosen = chosen });
    }

    protected static string Layer(StyleLayer layer) => layer.ToString().ToLowerInvariant();
}

/// <summary>Options for showing a voice.</summary>
public sealed class VoiceShowSettings : VoiceSettings
{
}

/// <summary>
/// Shows the guide definition and the rules in force, layer by layer.
/// </summary>
[Description("Show your learned voice: the guide it was learned against and the rules in force.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "voice writing style prose how I write rules learned profile")]
public sealed class VoiceShowCommand : VoiceCommandBase<VoiceShowSettings>
{
    public VoiceShowCommand(
        VoiceStore store,
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(store, instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceShowSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var resolved = await ResolveAsync(settings, cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var (guide, project, layers) = resolved.Value!;

        var rules = VoiceStore.Resolve(layers);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                guide,
                project = project?.Entry.Slug,
                layers = layers.Select(l => new
                {
                    layer = Layer(l.Layer),
                    name = l.Name,
                    path = l.Path,
                    exists = l.Profile is not null,
                    learnedAgainst = l.Profile?.Guide,
                    rules = l.Profile?.Rules.Count ?? 0,
                }),
                rules = rules.Select(r => new
                {
                    id = r.Rule.Id,
                    statement = r.Rule.Statement,
                    layer = Layer(r.Layer),
                    check = r.Rule.Check?.Type,
                    surfaces = r.Rule.Surfaces,
                    citations = r.Rule.Citations.Count,
                }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(guide.Reference)}[/]  [dim]{Markup.Escape(guide.Summary)}[/]");
        output.WriteLine(
            $"[dim]Proposes a rule on {guide.Evidence.Citations} citations from {guide.Evidence.Sources} sources. "
            + $"Checks: {Markup.Escape(string.Join(", ", guide.CheckTypes))}.[/]");
        output.WriteBlankLine();

        foreach (var layer in layers)
        {
            var state = layer.Profile is { } profile
                ? $"{profile.Rules.Count} rule(s), learned against {Markup.Escape(profile.Guide)}"
                : "nothing learned yet";

            output.WriteLine($"[cyan]{Layer(layer.Layer)}[/]  [dim]{state}[/]");
            output.WriteLine($"  [dim]{Markup.Escape(layer.Path)}[/]");
        }

        if (rules.Count == 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]No rules in force yet.[/]");

            return CommandOutput.Success();
        }

        output.WriteBlankLine();

        foreach (var rule in rules)
        {
            var how = rule.Rule.Check is { } check ? check.Type : "judged";

            output.WriteLine(
                $"[cyan]{Markup.Escape(rule.Rule.Id)}[/]  [dim]{Layer(rule.Layer)}, {Markup.Escape(how)}, "
                + $"{rule.Rule.Citations.Count} citation(s)[/]");
            output.WriteLine($"  {Markup.Escape(rule.Rule.Statement)}");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Options for checking writing against the voice.</summary>
public sealed class VoiceCheckSettings : VoiceSettings
{
    [CommandArgument(0, "[FILES]")]
    [Description("Files to check. Leave out and pass --message or --stdin to check a message.")]
    public string[] Files { get; init; } = [];

    [CommandOption("--surface <SURFACE>")]
    [Description("What the writing is: doc, commit or pr. Files default to doc, a message to commit.")]
    public string? Surface { get; init; }

    [CommandOption("--message <TEXT>")]
    [Description("A commit message or pull request description to check.")]
    public string? Message { get; init; }

    [CommandOption("--stdin")]
    [Description("Read the writing to check from standard input.")]
    public bool Stdin { get; init; }
}

/// <summary>
/// Checks writing against the rules in force, using only the typed checks.
/// </summary>
/// <remarks>
/// <para>
/// The shared core: the docs audit and the drafting prompts both ask this, so
/// they cannot come to different answers about the same text. Its JSON is the
/// contract they read.
/// </para>
/// <para>
/// It reports and always succeeds. A newly learned rule should not block work
/// on its first day, and nothing is promoted to a gate until there are
/// re-learns to promote it on.
/// </para>
/// </remarks>
[Description("Check a file or message against your learned voice. Reports; never fails on a finding.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "check voice writing style commit message docs pull request prose rules",
    Example = "voice check --message \"Fix the thing\"")]
public sealed class VoiceCheckCommand : VoiceCommandBase<VoiceCheckSettings>
{
    public VoiceCheckCommand(
        VoiceStore store,
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(store, instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceCheckSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var given = (settings.Files.Length > 0 ? 1 : 0) + (settings.Message is not null ? 1 : 0) + (settings.Stdin ? 1 : 0);

        if (given != 1)
        {
            return output.Fail(
                "Give one of: files to check, --message, or --stdin.", ExitCode.InvalidArguments);
        }

        var resolved = await ResolveAsync(settings, cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var (guide, project, layers) = resolved.Value!;

        // One guide when one is named; otherwise the whole voice, since the
        // rendered voice tells an agent to check against everything learned.
        IReadOnlyList<VoiceGuide> guides = [guide];
        List<Loadout.Models.Voice.VoiceRule> rules;

        if (settings.Guide is null)
        {
            var current = await Store.CurrentGuidesAsync(cancellationToken).ConfigureAwait(false);
            var inForce = current.Failed
                ? Loadout.Models.Results.OperationResult<IReadOnlyList<ResolvedVoiceRule>>.Fail(current.Error!, current.ExitCode)
                : await Store.RulesInForceAsync(resolved.Value!.Chosen, project?.Entry.Slug, cancellationToken).ConfigureAwait(false);

            if (inForce.Failed)
            {
                return output.Fail(inForce);
            }

            guides = current.Value!;
            rules = inForce.Value!.Select(r => r.Rule).ToList();
        }
        else
        {
            rules = VoiceStore.Resolve(layers).Select(r => r.Rule).ToList();
        }

        var surface = (settings.Surface ?? (settings.Files.Length > 0 ? VoiceGuides.Document : VoiceGuides.Commit))
            .Trim().ToLowerInvariant();

        var surfaces = guides.SelectMany(g => g.Surfaces).Distinct(StringComparer.Ordinal).ToList();

        if (!surfaces.Contains(surface, StringComparer.Ordinal))
        {
            return output.Fail(
                $"'{surface}' is not a surface {(guides.Count == 1 ? $"the {guide.Id} guide" : "any guide")} knows. "
                + $"Known: {string.Join(", ", surfaces)}.",
                ExitCode.InvalidArguments);
        }
        var pieces = new List<(string Source, string Text)>();

        if (settings.Message is { } message)
        {
            pieces.Add(("message", message));
        }
        else if (settings.Stdin)
        {
            pieces.Add(("stdin", await System.Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false)));
        }
        else
        {
            var root = await RepositoryAsync(project, settings.Repo, cancellationToken).ConfigureAwait(false)
                ?? Invocation.CurrentDirectory;

            foreach (var file in settings.Files)
            {
                var path = Path.GetFullPath(file, Invocation.CurrentDirectory);

                if (!File.Exists(path))
                {
                    return output.Fail($"There is no file at '{file}'.", ExitCode.InvalidArguments);
                }

                var shown = Path.GetRelativePath(root, path) is var relative && !relative.StartsWith("..", StringComparison.Ordinal)
                    ? relative.Replace(Path.DirectorySeparatorChar, '/')
                    : path;

                pieces.Add((shown, await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)));
            }
        }

        var results = pieces.Select(p => (p.Source, Result: VoiceChecker.Check(p.Text, surface, rules))).ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                guides = guides.Select(g => g.Reference),
                surface,
                rules = rules.Count,
                checks = results.Select(r => new
                {
                    source = r.Source,
                    findings = r.Result.Findings.Select(f => new
                    {
                        rule = f.Rule,
                        type = f.Type,
                        line = f.Line,
                        detail = f.Detail,
                        gates = f.Gates,
                    }),
                    @unchecked = r.Result.Unchecked.Select(u => new { rule = u.Rule, reason = u.Reason }),
                }),
            });

            return CommandOutput.Success();
        }

        if (rules.Count == 0)
        {
            output.WriteLine("[dim]No voice rules in force, so there is nothing to check against.[/]");

            return CommandOutput.Success();
        }

        var total = 0;

        foreach (var (source, result) in results)
        {
            foreach (var finding in result.Findings)
            {
                total++;
                output.WriteLine(
                    $"[yellow]{Markup.Escape(source)}:{finding.Line}[/]  [dim]{Markup.Escape(finding.Rule)}[/]  "
                    + Markup.Escape(finding.Detail));
            }

            foreach (var (rule, reason) in result.Unchecked.Distinct())
            {
                output.WriteLine($"[dim]{Markup.Escape(rule)} not checked: {Markup.Escape(reason)}.[/]");
            }
        }

        output.WriteLine(total == 0
            ? $"[green]Nothing breaks the {rules.Count} rule(s) in force.[/]"
            : $"[dim]{total} finding(s) against {rules.Count} rule(s). None of them gates yet.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Options for marking writing as your own.</summary>
public sealed class VoiceMarkSettings : VoiceSettings
{
    [CommandArgument(0, "[TARGETS]")]
    [Description("Files, pull requests as pr:123, commits, or a commit range such as main~20..main.")]
    public string[] Targets { get; init; } = [];

    [CommandOption("--remove")]
    [Description("Take the marks off instead.")]
    public bool Remove { get; init; }

    [CommandOption("--lapsed")]
    [Description("Re-confirm every lapsed mark in this repository, pinning it to the text as it is now.")]
    public bool Lapsed { get; init; }

    [CommandOption("--list")]
    [Description("List this repository's marks and whether each still holds.")]
    public bool List { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Mark more than one thing without being asked.")]
    public bool Yes { get; init; }
}

/// <summary>
/// Marks writing as hand-written, so a learn run counts it as evidence.
/// </summary>
/// <remarks>
/// <para>
/// Only marked text counts, because most of what is in a repository was not
/// written by the person whose voice is being learned: a tool, an agent or a
/// colleague wrote it. A mark is the person saying "this one is mine".
/// </para>
/// <para>
/// The list is kept on this machine and nothing is written to the repository,
/// so the provenance stays out of git. A file's or pull request's mark is
/// pinned to its text and lapses when the text changes; <c>--lapsed</c> re-confirms them in one go.
/// </para>
/// </remarks>
[Description("Mark files, pull requests or commits as written by you, so learning counts them.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "mark hand-written my own writing commit docs evidence voice lapsed",
    Mutates = true,
    Example = "voice mark docs/guide.md")]
public sealed class VoiceMarkCommand : VoiceCommandBase<VoiceMarkSettings>
{
    private readonly VoiceMarks _marks;

    public VoiceMarkCommand(
        VoiceMarks marks,
        VoiceStore store,
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(store, instructions, workspace, projects, git, console)
    {
        _marks = marks;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceMarkSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var modes = (settings.Targets.Length > 0 ? 1 : 0) + (settings.Lapsed ? 1 : 0) + (settings.List ? 1 : 0);

        if (modes != 1 || (settings.Remove && settings.Targets.Length == 0))
        {
            return output.Fail(
                "Give what to mark, or one of --lapsed and --list. --remove needs what to unmark.",
                ExitCode.InvalidArguments);
        }

        var project = await ProjectAsync(settings.Project, settings.Repo, cancellationToken).ConfigureAwait(false);
        var repository = await RepositoryAsync(project, settings.Repo, cancellationToken).ConfigureAwait(false)
            ?? Invocation.CurrentDirectory;

        if (settings.List)
        {
            return await ListAsync(output, repository, cancellationToken).ConfigureAwait(false);
        }

        List<VoiceMarkCandidate> candidates;

        if (settings.Lapsed)
        {
            var lapsed = await _marks.LapsedAsync(repository, cancellationToken).ConfigureAwait(false);

            if (lapsed.Failed)
            {
                return output.Fail(lapsed);
            }

            candidates = [.. lapsed.Value!];

            if (candidates.Count == 0)
            {
                output.WriteLine("[dim]No marks have lapsed in this repository.[/]");

                return CommandOutput.Success();
            }
        }
        else
        {
            candidates = [];

            foreach (var target in settings.Targets)
            {
                var found = await _marks.CandidatesAsync(repository, target, cancellationToken).ConfigureAwait(false);

                if (found.Failed)
                {
                    return output.Fail(found);
                }

                candidates.AddRange(found.Value!);
            }

            if (candidates.Count == 0)
            {
                output.WriteLine("[dim]That names nothing to mark.[/]");

                return CommandOutput.Success();
            }
        }

        var verb = settings.Remove ? "Unmark" : settings.Lapsed ? "Re-confirm" : "Mark";

        if (settings.DryRun)
        {
            output.WriteLine($"[bold]Would {verb.ToLowerInvariant()}[/] {candidates.Count} item(s). Nothing was written.");
            Show(output, candidates);

            return CommandOutput.Success();
        }

        // More than one is a bulk mark, and a range can sweep in commits that
        // were not written by hand, so the list is shown before it is kept.
        if (candidates.Count > 1 && !settings.Yes)
        {
            if (!settings.AllowsPrompting)
            {
                return output.Fail(
                    $"That is {candidates.Count} items. Pass --yes to {verb.ToLowerInvariant()} them all without being asked.",
                    ExitCode.InvalidArguments);
            }

            Show(output, candidates);

            if (!Console.Confirm($"{verb} these {candidates.Count} as written by you?", defaultValue: false))
            {
                output.WriteLine("[dim]Cancelled. Nothing was written.[/]");

                return CommandOutput.Success();
            }
        }

        if (settings.Remove)
        {
            var removed = await _marks.UnmarkAsync(repository, candidates, cancellationToken).ConfigureAwait(false);

            if (removed.Failed)
            {
                return output.Fail(removed);
            }

            if (output.IsJson)
            {
                output.WriteJson(new { removed = removed.Value });
            }
            else
            {
                output.WriteLine($"[green]-[/] Took {removed.Value} mark(s) off.");
            }

            return CommandOutput.Success();
        }

        var marked = await _marks.MarkAsync(repository, candidates, cancellationToken).ConfigureAwait(false);

        if (marked.Failed)
        {
            return output.Fail(marked);
        }

        var (added, confirmed) = marked.Value;

        if (output.IsJson)
        {
            output.WriteJson(new { added, confirmed });
        }
        else
        {
            output.WriteLine($"[green]+[/] Marked {added} new, re-confirmed {confirmed}.");
            output.WriteLine("[dim]Kept on this machine only. Nothing was written to the repository.[/]");
        }

        return CommandOutput.Success();
    }

    private async Task<int> ListAsync(CommandOutput output, string repository, CancellationToken ct)
    {
        var standing = await _marks.StandingAsync(repository, ct).ConfigureAwait(false);

        if (standing.Failed)
        {
            return output.Fail(standing);
        }

        var marks = standing.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                repository,
                marks = marks.Select(m => new
                {
                    kind = m.Mark.Kind.ToString().ToLowerInvariant(),
                    reference = m.Mark.Reference,
                    state = m.State.ToString().ToLowerInvariant(),
                    marked = m.Mark.Marked,
                }),
            });

            return CommandOutput.Success();
        }

        if (marks.Count == 0)
        {
            output.WriteLine("[dim]Nothing in this repository is marked as yours yet.[/]");

            return CommandOutput.Success();
        }

        foreach (var mark in marks)
        {
            var state = mark.State switch
            {
                VoiceMarkState.Holds => "[green]holds[/]",
                VoiceMarkState.Lapsed => "[yellow]lapsed[/]",
                _ => "[red]missing[/]",
            };

            var reference = mark.Mark.Kind == VoiceMarkKind.Commit ? mark.Mark.Reference[..Math.Min(12, mark.Mark.Reference.Length)] : mark.Mark.Reference;

            output.WriteLine($"{state}  [dim]{mark.Mark.Kind.ToString().ToLowerInvariant()}[/]  {Markup.Escape(reference)}");
        }

        if (marks.Any(m => m.State == VoiceMarkState.Lapsed))
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]A lapsed mark's text has changed since. Re-confirm with: loadout voice mark --lapsed[/]");
        }

        return CommandOutput.Success();
    }

    private static void Show(CommandOutput output, IReadOnlyList<VoiceMarkCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            var reference = candidate.Kind == VoiceMarkKind.Commit
                ? candidate.Reference[..Math.Min(12, candidate.Reference.Length)]
                : candidate.Reference;

            output.WriteLine($"  [dim]{Markup.Escape(reference)}[/]  {Markup.Escape(candidate.Summary)}");
        }
    }
}

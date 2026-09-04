using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>Shared plumbing for the instruction commands.</summary>
/// <remarks>
/// The library layers built-in, workspace and project specialists, so every one
/// of these needs the same two things resolved the same way: which workspace,
/// and which project. Doing it once here is what keeps <c>list</c> and
/// <c>explain</c> talking about the same library.
/// </remarks>
public abstract class InstructionsCommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : GlobalSettings
{
    protected InstructionsCommandBase(
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
    {
        Instructions = instructions;
        Workspace = workspace;
        Projects = projects;
        Git = git;
        Console = console;
    }

    protected IInstructionService Instructions { get; }

    protected IWorkspaceManager Workspace { get; }

    protected IProjectService Projects { get; }

    protected IGitManager Git { get; }

    protected IAnsiConsole Console { get; }

    /// <summary>The workspace clone, or null when there is not one.</summary>
    protected string? WorkspacePath => Workspace.IsAvailable() ? Workspace.LocalPath : null;

    /// <summary>
    /// The project in play: the one named, or the one the current directory is
    /// in. Null is an ordinary answer — the built-in library still loads.
    /// </summary>
    protected async Task<ProjectResolution?> ProjectAsync(
        string? handle,
        string? repo = null,
        CancellationToken ct = default)
    {
        var result = handle is { Length: > 0 }
            ? await Projects.ResolveAsync(handle, ct).ConfigureAwait(false)
            : await Projects.ResolveFromDirectoryAsync(
                repo ?? Directory.GetCurrentDirectory(), ct).ConfigureAwait(false);

        return result.Succeeded ? result.Value : null;
    }

    /// <summary>
    /// The code to gather evidence from.
    /// </summary>
    /// <remarks>
    /// A registered project's path when there is one, and otherwise wherever
    /// you are. Requiring registration first was wrong: running this in an
    /// obvious C# repository and being told nothing was detected reads as a
    /// broken feature rather than an unregistered directory, and it is the
    /// first thing anybody tries.
    /// </remarks>
    protected async Task<string?> RepositoryAsync(
        ProjectResolution? project,
        string? repo = null,
        CancellationToken ct = default)
    {
        if (project?.LocalPath is { Length: > 0 } known)
        {
            return known;
        }

        var here = repo ?? Directory.GetCurrentDirectory();

        // The repository root rather than the subdirectory somebody happens to
        // be standing in, so the answer does not change with where they ran it.
        var root = await Git.FindRepositoryRootAsync(here, ct).ConfigureAwait(false);

        return root.Succeeded && root.Value is { Length: > 0 } found ? found : here;
    }

    /// <summary>Renders one specialist's identity the same way everywhere.</summary>
    protected static string Origin(SpecialistOrigin origin) => origin switch
    {
        SpecialistOrigin.Workspace => "workspace",
        SpecialistOrigin.Project => "project",

        // Named, because where a specialist came from is the whole question a
        // reader has about one that arrived from somebody else's repository.
        // Falling through to "built-in" said the opposite of the truth.
        SpecialistOrigin.Pack => "pack",
        _ => "built-in",
    };
}

/// <summary>Options for listing.</summary>
public sealed class InstructionsListSettings : GlobalSettings
{
    [CommandArgument(0, "[PROJECT]")]
    [Description("Project whose library to read. Defaults to the repository you are in.")]
    public string? Project { get; init; }

    [CommandOption("--kind <KIND>")]
    [Description("Only one kind: foundation, mode, language, framework, database, platform, cloud, function or skill.")]
    public string? Kind { get; init; }
}

/// <summary>
/// Lists the specialists available.
/// </summary>
[Description("List the specialists and skills available to a project.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "specialists skills expertise available library postgresql security performance")]
public sealed class InstructionsListCommand : InstructionsCommandBase<InstructionsListSettings>
{
    public InstructionsListCommand(
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, InstructionsListSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        SpecialistKind? wanted = null;

        if (settings.Kind is { Length: > 0 } named)
        {
            if (!Enum.TryParse<SpecialistKind>(named, ignoreCase: true, out var parsed))
            {
                var known = string.Join(
                    ", ", Enum.GetNames<SpecialistKind>().Select(n => n.ToLowerInvariant()));

                return output.Fail($"'{named}' is not a kind. Use one of: {known}.",
                    ExitCode.InvalidArguments);
            }

            wanted = parsed;
        }

        var project = await ProjectAsync(settings.Project, settings.Repo).ConfigureAwait(false);

        var catalogue = await Instructions
            .LibraryAsync(WorkspacePath, project?.Entry.Slug)
            .ConfigureAwait(false);

        var specialists = catalogue.All
            .Where(s => wanted is null || s.Kind == wanted)
            .OrderBy(s => (int)s.Kind)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                count = specialists.Count,
                specialists = specialists.Select(s => new
                {
                    id = s.Id,
                    kind = s.Kind.ToString().ToLowerInvariant(),
                    title = s.Title,
                    summary = s.Summary,
                    origin = Origin(s.Origin),
                    bytes = s.Bytes,
                    estimatedTokens = s.EstimatedTokens,
                    always = s.Activation.Always,
                }),
                findings = catalogue.Findings.Select(f => new
                {
                    specialist = f.Rule,
                    severity = f.Severity.ToString().ToLowerInvariant(),
                    kind = f.Kind,
                    detail = f.Detail,
                }),
            });

            return CommandOutput.Success();
        }

        if (specialists.Count == 0)
        {
            output.WriteLine("[dim]No specialists matched.[/]");

            return CommandOutput.Success();
        }

        SpecialistKind? heading = null;

        foreach (var specialist in specialists)
        {
            if (heading != specialist.Kind)
            {
                heading = specialist.Kind;

                output.WriteBlankLine();
                output.WriteLine($"[bold]{specialist.Kind.ToString().ToLowerInvariant()}[/]");
            }

            var origin = specialist.Origin == SpecialistOrigin.BuiltIn
                ? string.Empty
                : $" [dim]({Origin(specialist.Origin)})[/]";

            output.WriteLine(
                $"  [cyan]{specialist.Id.EscapeMarkup()}[/]{origin}  "
                + $"[dim]{specialist.Summary.EscapeMarkup()}[/]");
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[dim]{specialists.Count} available. "
            + "Inspect one with loadout instructions show <id>.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Options for showing one specialist.</summary>
public sealed class InstructionsShowSettings : GlobalSettings
{
    [CommandArgument(0, "<SPECIALIST>")]
    [Description("Specialist id, for example database.postgresql.")]
    public string Specialist { get; init; } = string.Empty;

    [CommandOption("--project <PROJECT>")]
    [Description("Project whose library to read. Defaults to the repository you are in.")]
    public string? Project { get; init; }
}

/// <summary>Shows one specialist in full, including what activates it.</summary>
[Description("Show one specialist: what it says, and what makes it relevant.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "specialist detail inspect guidance")]
public sealed class InstructionsShowCommand : InstructionsCommandBase<InstructionsShowSettings>
{
    public InstructionsShowCommand(
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, InstructionsShowSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var project = await ProjectAsync(settings.Project, settings.Repo).ConfigureAwait(false);

        var catalogue = await Instructions
            .LibraryAsync(WorkspacePath, project?.Entry.Slug)
            .ConfigureAwait(false);

        if (catalogue.Find(settings.Specialist) is not { } specialist)
        {
            return output.Fail(
                $"No specialist named '{settings.Specialist}'. "
                + "Run 'loadout instructions list' to see what there is.",
                ExitCode.ProjectNotFound);
        }

        var activation = specialist.Activation;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                id = specialist.Id,
                kind = specialist.Kind.ToString().ToLowerInvariant(),
                title = specialist.Title,
                summary = specialist.Summary,
                origin = Origin(specialist.Origin),
                path = specialist.Path,
                bytes = specialist.Bytes,
                estimatedTokens = specialist.EstimatedTokens,
                activation = new
                {
                    always = activation.Always,
                    globs = activation.GlobList,
                    dependencies = activation.DependencyList,
                    taskPhrases = activation.TaskPhraseList,
                    requires = activation.RequiresList,
                    capabilities = activation.CapabilityList,
                    modes = activation.ModeList,
                },
                body = specialist.Body,
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{specialist.Title.EscapeMarkup()}[/]  [dim]{specialist.Id}[/]");
        output.WriteLine($"[dim]{Origin(specialist.Origin)}, about {specialist.EstimatedTokens:N0} tokens[/]");
        output.WriteBlankLine();

        output.WriteLine("[bold]Loads when[/]");

        if (activation.Always)
        {
            output.WriteLine("  always");
        }
        else if (specialist.Kind == SpecialistKind.Mode)
        {
            // Modes are chosen rather than detected, so an empty section here
            // reads as a specialist nothing can ever reach — which would be a
            // fault, and is not what is happening.
            output.WriteLine("  chosen with --mode " + specialist.Name);
        }
        else if (activation.TaskPhraseList.Count == 0
            && activation.DependencyList.Count == 0
            && activation.GlobList.Count == 0)
        {
            output.WriteLine("  only when named with --specialist");
        }

        Write(output, "  task mentions", activation.TaskPhraseList);
        Write(output, "  dependency", activation.DependencyList);
        Write(output, "  files match", activation.GlobList);
        Write(output, "  requires", activation.RequiresList);
        Write(output, "  only in mode", activation.ModeList);
        Write(output, "  agent supports", activation.CapabilityList);

        output.WriteBlankLine();
        output.WriteLine(specialist.Body.EscapeMarkup());

        return CommandOutput.Success();
    }

    private static void Write(CommandOutput output, string label, IReadOnlyList<string> values)
    {
        if (values.Count > 0)
        {
            output.WriteLine($"[dim]{label}:[/] {string.Join(", ", values).EscapeMarkup()}");
        }
    }
}

/// <summary>Options for auditing a project against its specialists.</summary>
public sealed class InstructionsAuditSettings : GlobalSettings
{
    [CommandOption("--project <PROJECT>")]
    [Description("Project to audit. Defaults to the repository you are in.")]
    public string? Project { get; init; }

    [CommandOption("--mode <MODE>")]
    [Description("Posture: advise, investigate, implement or review.")]
    public string? Mode { get; init; }

    [CommandOption("--strict")]
    [Description("Exit non-zero when anything is found, for use in CI.")]
    public bool Strict { get; init; }
}

/// <summary>Options for explaining a resolution.</summary>
public sealed class InstructionsExplainSettings : GlobalSettings
{
    [CommandArgument(0, "[TASK]")]
    [Description("What you would be asking the agent to do. Drives most of the selection.")]
    public string? Task { get; init; }

    [CommandOption("--project <PROJECT>")]
    [Description("Project to explain for. Defaults to the repository you are in.")]
    public string? Project { get; init; }

    [CommandOption("--mode <MODE>")]
    [Description("Posture: advise, investigate, implement or review.")]
    public string? Mode { get; init; }

    [CommandOption("--specialist <ID>")]
    [Description("Load this specialist whatever the evidence says. Repeatable.")]
    public string[] Specialist { get; init; } = [];

    [CommandOption("--without <ID>")]
    [Description("Never load this specialist. Repeatable.")]
    public string[] Without { get; init; } = [];

    [CommandOption("--against-mode <MODE>")]
    [Description("Compare against this mode instead, and show only what changes.")]
    public string? AgainstMode { get; init; }

    [CommandOption("--against-task <TEXT>")]
    [Description("Compare against this wording of the task instead.")]
    public string? AgainstTask { get; init; }

    [CommandOption("--against-profile <NAME>")]
    [Description("Compare against this context profile instead.")]
    public string? AgainstProfile { get; init; }

    [CommandOption("--against-without <ID>")]
    [Description("Compare against a run that also excludes this specialist. Repeatable.")]
    public string[] AgainstWithout { get; init; } = [];

    /// <summary>
    /// Whether a second configuration was described at all.
    /// </summary>
    /// <remarks>
    /// Separate options rather than one <c>--against</c> taking a little
    /// language of its own. Four obvious flags beat a syntax that has to be
    /// documented, parsed and explained when somebody spells it wrong.
    /// </remarks>
    public bool HasComparison =>
        AgainstMode is not null
        || AgainstTask is not null
        || AgainstProfile is not null
        || AgainstWithout.Length > 0;
}

/// <summary>
/// Answers the question the whole system exists to answer.
/// </summary>
/// <remarks>
/// "Why did Loadout give this agent these instructions?" Resolution runs through
/// the same service the launch path uses, so what this prints is what a launch
/// would actually compose — an explanation produced by a second code path would
/// eventually disagree with reality and be believed anyway.
/// </remarks>
[Description("Explain which specialists a task would load, and why.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "why these instructions explain specialists selection reason context budget")]
public sealed class InstructionsExplainCommand : InstructionsCommandBase<InstructionsExplainSettings>
{
    private readonly IRuleService _rules;
    private readonly IMemoryService _memory;

    public InstructionsExplainCommand(
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IRuleService rules,
        IMemoryService memory,
        IAnsiConsole console)
        : base(instructions, workspace, projects, git, console)
    {
        _rules = rules;
        _memory = memory;
    }

    /// <summary>
    /// What the other two layers cost, so the report can add all three.
    /// </summary>
    /// <remarks>
    /// Read here rather than by the resolver, which has no business knowing
    /// about rules or memory. Absent for a project with no workspace, and a
    /// missing layer counts as nothing rather than as unknown: a session with no
    /// memory really is paying nothing for it.
    /// </remarks>
    private async Task<(long Always, long Scoped, long MemoryIndex)> LayersAsync(
        string? slug,
        CancellationToken ct)
    {
        if (slug is null || WorkspacePath is null)
        {
            return (0, 0, 0);
        }

        var always = 0L;
        var scoped = 0L;

        var rules = await _rules.LoadAsync(WorkspacePath, slug, ct).ConfigureAwait(false);

        if (rules.Succeeded)
        {
            always = rules.Value!
                .Where(rule => rule.AlwaysApply || rule.IsUnscoped)
                .Sum(rule => rule.Bytes);

            scoped = rules.Value!
                .Where(rule => !rule.AlwaysApply && !rule.IsUnscoped)
                .Sum(rule => rule.Bytes);
        }

        var index = await _memory.ReadIndexAsync(WorkspacePath, slug, ct).ConfigureAwait(false);

        return (always, scoped, index.Value?.Length ?? 0);
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        InstructionsExplainSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var project = await ProjectAsync(settings.Project, settings.Repo).ConfigureAwait(false);

        var manifest = project is not null && WorkspacePath is not null
            ? (await Workspace.ReadProjectAsync(project.Entry.Slug).ConfigureAwait(false)).Value
            : null;

        var resolved = await Instructions.ResolveAsync(new InstructionRequest(
            manifest,
            await RepositoryAsync(project, settings.Repo).ConfigureAwait(false),
            WorkspacePath,
            settings.Agent ?? manifest?.Agents.Default ?? "claude",
            ProfileName: settings.Profile,
            Task: settings.Task,
            Explicit: settings.Specialist,
            Excluded: settings.Without,
            Mode: settings.Mode)).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var effective = resolved.Value!;

        if (settings.HasComparison)
        {
            var against = await Instructions.ResolveAsync(new InstructionRequest(
                manifest,
                await RepositoryAsync(project, settings.Repo).ConfigureAwait(false),
                WorkspacePath,
                settings.Agent ?? manifest?.Agents.Default ?? "claude",
                ProfileName: settings.AgainstProfile ?? settings.Profile,
                Task: settings.AgainstTask ?? settings.Task,
                Explicit: settings.Specialist,

                // Added to the exclusions rather than replacing them: the second
                // configuration is the first with a change made to it, and
                // silently dropping what the first was told to leave out would
                // make the difference include changes nobody asked for.
                Excluded: [.. settings.Without, .. settings.AgainstWithout],
                Mode: settings.AgainstMode ?? settings.Mode)).ConfigureAwait(false);

            if (against.Failed)
            {
                return output.Fail(against);
            }

            var diff = InstructionDiff.Between(effective, against.Value!);

            if (output.IsJson)
            {
                output.WriteJson(diff);

                return CommandOutput.Success();
            }

            RenderDiff(output, diff);

            return CommandOutput.Success();
        }

        if (output.IsJson)
        {
            output.WriteJson(Describe(effective, project?.Entry.Slug, settings.Task));

            return CommandOutput.Success();
        }

        var counted = await LayersAsync(project?.Entry.Slug, cancellationToken).ConfigureAwait(false);

        Render(
            output,
            effective,
            settings.Task,
            ContextBudget.From(effective, counted.Always, counted.Scoped, counted.MemoryIndex));

        return CommandOutput.Success();
    }

    /// <summary>
    /// Only what differs.
    /// </summary>
    /// <remarks>
    /// The point of asking for a comparison is that the forty lines both sides
    /// share are not the question. They are counted and not listed.
    /// </remarks>
    private static void RenderDiff(CommandOutput output, InstructionDiff diff)
    {
        if (diff.IsSame)
        {
            output.WriteLine("[green]+[/] Both compose the same specialists.");
        }

        foreach (var change in diff.Removed)
        {
            output.WriteLine(
                $"[red]-[/] {Markup.Escape(change.Id),-34} "
                + $"[dim]{change.EstimatedTokens,6:N0}  {Markup.Escape(change.Reason)}[/]");
        }

        foreach (var change in diff.Added)
        {
            output.WriteLine(
                $"[green]+[/] {Markup.Escape(change.Id),-34} "
                + $"[dim]{change.EstimatedTokens,6:N0}  {Markup.Escape(change.Reason)}[/]");
        }

        output.WriteBlankLine();
        output.WriteLine($"  [dim]Unchanged[/]  {diff.Kept}");
        output.WriteLine(
            $"  [dim]Estimated[/]  {diff.TokensBefore:N0} to {diff.TokensAfter:N0} "
            + $"({(diff.TokenDelta >= 0 ? "+" : string.Empty)}{diff.TokenDelta:N0})");
    }

    /// <summary>
    /// The machine-readable shape.
    /// </summary>
    /// <remarks>
    /// Treated as a compatibility contract from here on. Anything reading this
    /// should be able to keep reading it, so fields are added rather than
    /// renamed.
    /// </remarks>
    internal static object Describe(
        EffectiveInstructions effective,
        string? slug,
        string? task) => new
        {
            project = slug,
            task,
            mode = effective.Mode,
            selected = effective.Selected.Select(Selection),
            omitted = effective.Omitted.Select(Selection),
            conflicts = effective.Conflicts.Select(c => new
            {
                subject = c.Subject,
                winner = c.WinnerId,
                loser = c.LoserId,
                reason = c.Reason,
            }),
            context = new
            {
                bytes = effective.Budget.Bytes,
                estimatedTokens = effective.Budget.EstimatedTokens,
                tokenBudget = effective.Budget.TokenBudget,
                usedFraction = effective.Budget.UsedFraction,
                overBudget = effective.Budget.IsOverBudget,
                nearBudget = effective.Budget.IsNearBudget,
            },
            evidenceTruncated = effective.EvidenceTruncated,
        };

    private static object Selection(SpecialistSelection selection) => new
    {
        id = selection.Specialist.Id,
        kind = selection.Specialist.Kind.ToString().ToLowerInvariant(),
        title = selection.Specialist.Title,
        trigger = selection.Trigger.ToString(),
        reason = selection.Reason,
        confidence = selection.Confidence,
        origin = selection.Specialist.Origin.ToString().ToLowerInvariant(),
        estimatedTokens = selection.Specialist.EstimatedTokens,
    };

    private static void Render(
        CommandOutput output,
        EffectiveInstructions effective,
        string? task,
        ContextBudget? layers)
    {
        output.WriteLine("[bold]Effective agent instructions[/]");

        if (task is { Length: > 0 })
        {
            output.WriteLine($"[dim]for: {task.EscapeMarkup()}[/]");
        }

        SpecialistKind? heading = null;

        foreach (var selection in effective.Selected)
        {
            if (heading != selection.Specialist.Kind)
            {
                heading = selection.Specialist.Kind;

                output.WriteBlankLine();
                output.WriteLine($"[bold]{heading.ToString()!.ToLowerInvariant()}[/]");
            }

            output.WriteLine(
                $"  [green]+[/] {selection.Specialist.Title.EscapeMarkup()} "
                + $"[dim]{selection.Specialist.Id}[/]");
            output.WriteLine($"      [dim]{selection.Reason.EscapeMarkup()}[/]");
        }

        if (effective.Omitted.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]Not loaded[/]");

            foreach (var omitted in effective.Omitted)
            {
                output.WriteLine(
                    $"  [dim]o {omitted.Specialist.Id.EscapeMarkup()} — "
                    + $"{omitted.Reason.EscapeMarkup()}[/]");
            }
        }

        output.WriteBlankLine();
        output.WriteLine("[bold]Context[/]");

        var budget = effective.Budget;

        if (layers is { } context)
        {
            // Every layer, in one unit, so the figure at the bottom means what
            // it says. They have been counted separately and in different units
            // until now — tokens for specialists, bytes for rules, nothing at
            // all for the memory index — so there was no answer to what a
            // session here costs.
            foreach (var layer in context.Layers.Where(layer => layer.EveryLaunch))
            {
                output.WriteLine(
                    $"  {layer.Name,-24} {layer.EstimatedTokens,7:N0}");
            }

            output.WriteLine($"  [bold]{"Every launch",-24} {context.EveryLaunchTokens,7:N0}[/]");

            if (context.OnDemandTokens > 0)
            {
                output.WriteLine(
                    $"  [dim]{"On demand",-24} {context.OnDemandTokens,7:N0}  "
                    + "scoped rules, loaded only when the work touches them[/]");
            }

            output.WriteBlankLine();
        }
        else
        {
            output.WriteLine($"  {"Specialists",-24} {budget.EstimatedTokens,7:N0}");
            output.WriteBlankLine();
        }

        if (budget.TokenBudget > 0)
        {
            var colour = budget.IsOverBudget ? "red" : budget.IsNearBudget ? "yellow" : "green";

            // Named for what it governs. The ceiling is enforced against the
            // specialist layer and nothing else — it is what the resolver
            // negotiates down to — so labelling it as the budget for the whole
            // context would be a promise the launcher does not keep.
            output.WriteLine($"  {"Budget (specialists)",-24} {budget.TokenBudget,7:N0}");
            output.WriteLine(
                $"  {"Usage",-24} [{colour}]{budget.UsedFraction * 100,6:N0}%[/]");
        }
        else
        {
            output.WriteLine("  [dim]No budget set.[/]");
        }

        if (effective.EvidenceTruncated)
        {
            // A partial scan produces an answer that looks exactly as confident
            // as a complete one. Saying so is the difference between "no C#
            // here" and "no C# in the part I looked at".
            output.WriteBlankLine();
            output.WriteLine(
                "[yellow]The repository was too large to scan in full, so what was detected "
                + "from files may be incomplete.[/]");
        }

        output.WriteBlankLine();

        if (effective.Conflicts.Count == 0)
        {
            output.WriteLine("[dim]Conflicts: none[/]");

            return;
        }

        output.WriteLine("[bold]Where guidance overlaps[/]");

        foreach (var conflict in effective.Conflicts)
        {
            output.WriteLine(
                $"  {conflict.Subject.EscapeMarkup()}: follow "
                + $"[cyan]{conflict.WinnerId.EscapeMarkup()}[/] over "
                + $"[dim]{conflict.LoserId.EscapeMarkup()}[/] ({conflict.Reason.EscapeMarkup()})");
        }
    }
}

/// <summary>Options for validating.</summary>
public sealed class InstructionsValidateSettings : GlobalSettings
{
    [CommandArgument(0, "[PROJECT]")]
    [Description("Project whose library to check. Defaults to the repository you are in.")]
    public string? Project { get; init; }

    [CommandOption("--strict")]
    [Description("Exit non-zero on warnings as well as errors, for use in CI.")]
    public bool Strict { get; init; }
}

/// <summary>Checks the specialist library for defects.</summary>
[Description("Check the specialist library for defects.")]
[CommandMeta(CommandCategory.Health, Intent = "validate specialists library broken check")]
public sealed class InstructionsValidateCommand : InstructionsCommandBase<InstructionsValidateSettings>
{
    public InstructionsValidateCommand(
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        InstructionsValidateSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var project = await ProjectAsync(settings.Project, settings.Repo).ConfigureAwait(false);

        var catalogue = await Instructions
            .LibraryAsync(WorkspacePath, project?.Entry.Slug)
            .ConfigureAwait(false);

        var errors = catalogue.Findings.Count(f => f.Severity == RuleFindingSeverity.Error);
        var warnings = catalogue.Findings.Count(f => f.Severity == RuleFindingSeverity.Warning);

        // A copy that has fallen behind the built-in it replaces. Nothing else
        // would ever say so: the copy wins by design, and it goes on winning
        // after the original has been improved.
        var stale = SpecialistOrigins.Stale(catalogue, Instructions.BuiltInText);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                specialists = catalogue.Specialists.Count,
                errors,
                warnings,
                stale = stale.Select(copy => new
                {
                    specialist = copy.Id,
                    path = copy.Path,
                    origin = copy.Origin.ToString().ToLowerInvariant(),
                }),
                findings = catalogue.Findings.Select(f => new
                {
                    specialist = f.Rule,
                    severity = f.Severity.ToString().ToLowerInvariant(),
                    kind = f.Kind,
                    detail = f.Detail,
                }),
            });

            return Verdict(errors, warnings, settings.Strict);
        }

        output.WriteLine($"[dim]{catalogue.Specialists.Count} specialists loaded.[/]");

        foreach (var finding in catalogue.Findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Rule, StringComparer.Ordinal))
        {
            var colour = finding.Severity switch
            {
                RuleFindingSeverity.Error => "red",
                RuleFindingSeverity.Warning => "yellow",
                _ => "dim",
            };

            output.WriteLine(
                $"[{colour}]{finding.Severity.ToString().ToLowerInvariant()}[/] "
                + $"{finding.Detail.EscapeMarkup()}");
        }

        foreach (var copy in stale)
        {
            output.WriteLine($"[yellow]stale[/] {SpecialistOrigins.Describe(copy).EscapeMarkup()}");
        }

        output.WriteBlankLine();

        // Staleness is not a defect and does not change the verdict: a copy
        // that has fallen behind is still a valid specialist, deliberately
        // chosen. It is said alongside rather than folded in, because "sound"
        // printed directly under a warning reads as a contradiction.
        var behind = stale.Count == 0
            ? string.Empty
            : $" {stale.Count} copy(s) behind the built-in they replace.";

        output.WriteLine(errors == 0 && warnings == 0
            ? $"[green]The specialist library is sound.[/][dim]{behind}[/]"
            : $"{errors} error(s), {warnings} warning(s).{behind}");

        return Verdict(errors, warnings, settings.Strict);
    }

    private static int Verdict(int errors, int warnings, bool strict) =>
        errors > 0 || (strict && warnings > 0)
            ? (int)ExitCode.GeneralFailure
            : CommandOutput.Success();
}

/// <summary>Settings for drafting a specialist.</summary>
public sealed class InstructionsNewSettings : GlobalSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Identifier, naming its layer first: skill.deploy-checklist, language.rust.")]
    public string Id { get; init; } = string.Empty;

    [CommandOption("--project <SLUG>")]
    [Description("Add it to one project's library rather than the workspace-wide one.")]
    public string? Project { get; init; }

    [CommandOption("--title <TITLE>")]
    [Description("What it is called. Derived from the identifier when not given.")]
    public string? Title { get; init; }

    [CommandOption("--summary <SUMMARY>")]
    [Description("One line saying what it is for.")]
    public string? Summary { get; init; }

    [CommandOption("--force")]
    [Description("Overwrite a specialist of that name if one is already there.")]
    public bool Force { get; init; }
}

/// <summary>
/// Writes the first draft of a specialist into the workspace or a project.
/// </summary>
/// <remarks>
/// The library has always been extensible and there was no way to extend it.
/// Adding one meant knowing the frontmatter vocabulary, knowing which of it
/// your layer uses, knowing the identifier and the kind have to agree, knowing
/// which directory it belongs in, and finding out you were wrong afterwards.
/// </remarks>
[Description("Draft a new specialist or skill in the workspace or a project.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "new specialist skill create add author scaffold")]
public sealed class InstructionsNewCommand : InstructionsCommandBase<InstructionsNewSettings>
{
    public InstructionsNewCommand(
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        InstructionsNewSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        // Measured only when the draft is going to sit beside a repository. A
        // language or a framework specialist is about the language, not about
        // whoever happens to be drafting it here, and filling one with this
        // project's habits would be the opposite of what the layer is for.
        var here = await ProjectAsync(settings.Project, settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        var measured = settings.Project is { Length: > 0 }
            ? ProjectConventions.Detect(
                await RepositoryAsync(here, settings.Repo, cancellationToken).ConfigureAwait(false)
                    ?? Directory.GetCurrentDirectory(),
                cancellationToken)
            : null;

        var drafted = SpecialistScaffold.Draft(
            settings.Id, settings.Title, settings.Summary, measured);

        if (drafted.Failed)
        {
            return output.Fail(drafted);
        }

        var draft = drafted.Value!;

        if (WorkspacePath is not { Length: > 0 } workspace)
        {
            return output.Fail(
                "There is no workspace on this machine, so there is nowhere to keep a specialist. "
                + "Set one up with: loadout setup",
                ExitCode.ConfigurationInvalid);
        }

        // Workspace-wide unless a project is named. The wider one is the
        // default because guidance that is only true of one repository is the
        // rarer thing to be writing.
        string root;
        string scope;

        if (settings.Project is { Length: > 0 } handle)
        {
            var project = await Projects.ResolveAsync(handle).ConfigureAwait(false);

            if (project.Failed)
            {
                return output.Fail(project.Error!, project.ExitCode);
            }

            root = Path.Combine(workspace, "projects", project.Value!.Entry.Slug, "specialists");
            scope = project.Value!.Entry.Name;
        }
        else
        {
            root = Path.Combine(workspace, "global", "specialists");
            scope = "the workspace";
        }

        var directory = Path.Combine(root, SpecialistScaffold.DirectoryFor(draft.Kind));
        var path = Path.Combine(directory, draft.FileName);

        if (File.Exists(path) && !settings.Force)
        {
            return output.Fail(
                $"'{path}' already exists. Pass --force to overwrite it.",
                ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { id = draft.Id, path, scope, wouldWrite = draft.Content });

                return CommandOutput.Success();
            }

            output.WriteLine($"[yellow]Would write[/] {path.EscapeMarkup()}");
            output.WriteBlankLine();
            Console.WriteLine(draft.Content);

            return CommandOutput.Success();
        }

        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, draft.Content).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return output.Fail($"Could not write '{path}': {ex.Message}", ExitCode.GeneralFailure);
        }

        // Read the library back rather than trusting the template. A draft that
        // does not load is worse than no command at all, because it is found
        // later and by somebody else.
        var catalogue = await Instructions
            .LibraryAsync(WorkspacePath, settings.Project)
            .ConfigureAwait(false);

        var loaded = catalogue.Specialists.ContainsKey(draft.Id);

        var problems = catalogue.Findings
            .Where(f => string.Equals(f.Rule, draft.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                id = draft.Id,
                kind = draft.Kind.ToString().ToLowerInvariant(),
                path,
                scope,
                loaded,
                findings = problems.Select(f => new
                {
                    severity = f.Severity.ToString().ToLowerInvariant(),
                    detail = f.Detail,
                }),
            });

            return loaded ? CommandOutput.Success() : (int)ExitCode.GeneralFailure;
        }

        output.WriteLine($"[green]Created[/] {draft.Id.EscapeMarkup()} in {scope.EscapeMarkup()}");
        output.WriteLine($"  [dim]{path.EscapeMarkup()}[/]");
        output.WriteBlankLine();

        if (!loaded)
        {
            output.WriteLine("[red]It was written but the library did not load it.[/]");

            foreach (var finding in problems)
            {
                output.WriteLine($"  [red]{finding.Detail.EscapeMarkup()}[/]");
            }

            return (int)ExitCode.GeneralFailure;
        }

        foreach (var finding in problems)
        {
            output.WriteLine($"  [yellow]{finding.Detail.EscapeMarkup()}[/]");
        }

        output.WriteLine("[dim]Write the guidance, then check it with:[/]");
        output.WriteLine($"[dim]  loadout instructions show {draft.Id}[/]");
        output.WriteLine("[dim]  loadout instructions validate[/]");

        return CommandOutput.Success();
    }
}

/// <summary>
/// Reports where a repository does something its own specialists advise
/// against.
/// </summary>
/// <remarks>
/// <para>
/// The specialists say how work here should be done, and nothing has ever
/// checked whether it is. This counts a small number of measurable rules and
/// reports what it finds, quoting the rule each time so the finding has a
/// source rather than being the tool's own opinion.
/// </para>
/// <para>
/// It reads and reports. It proposes no edit and makes none: what to do about a
/// deviation is a judgement about a codebase, and putting the question in front
/// of somebody is the whole job.
/// </para>
/// </remarks>
[Description("Report where a project departs from what its specialists ask for.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "conventions audit deviations standards compliance check code")]
public sealed class InstructionsAuditCommand : InstructionsCommandBase<InstructionsAuditSettings>
{
    public InstructionsAuditCommand(
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        InstructionsAuditSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var project = await ProjectAsync(settings.Project, settings.Repo).ConfigureAwait(false);
        var repository = await RepositoryAsync(project, settings.Repo).ConfigureAwait(false);

        if (repository is null)
        {
            return output.Fail(
                "There is no repository here to audit. Name a project, or run this inside one.",
                ExitCode.RepositoryUnavailable);
        }

        var manifest = project is not null && WorkspacePath is not null
            ? (await Workspace.ReadProjectAsync(project.Entry.Slug, cancellationToken)
                .ConfigureAwait(false)).Value
            : null;

        // Asked of the same resolver a launch uses. A check for a language the
        // project is not written in is noise, and its specialist would not be
        // loaded either — so what applies here is what would apply then.
        var resolved = await Instructions.ResolveAsync(new InstructionRequest(
            manifest,
            repository,
            WorkspacePath,
            settings.Agent ?? manifest?.Agents.Default ?? "claude",
            Mode: settings.Mode), cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var applicable = resolved.Value!.Selected
            .Select(selection => selection.Specialist.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var findings = ConventionAuditor.Audit(
            repository, applicable.Contains, cancellationToken);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = project?.Entry.Slug,
                repository,
                checks = ConventionAuditor.Checks.Count,
                applicable = applicable.Count,
                findings = findings.Select(finding => new
                {
                    specialist = finding.Check.SpecialistId,
                    rule = finding.Check.Rule,
                    occurrences = finding.Occurrences,
                    filesRead = finding.FilesRead,
                    caveat = finding.Check.Caveat,
                    files = finding.Files.Select(file => new { path = file.Path, count = file.Count }),
                }),
            });

            return findings.Count > 0 && settings.Strict
                ? (int)ExitCode.GeneralFailure
                : CommandOutput.Success();
        }

        if (findings.Count == 0)
        {
            output.WriteLine(
                "[green]Nothing to report.[/] "
                + $"[dim]{ConventionAuditor.Checks.Count} check(s), of which the ones for this "
                + "project's specialists found no departures.[/]");

            return CommandOutput.Success();
        }

        foreach (var finding in findings)
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"[yellow]{finding.Occurrences}[/] in {finding.FilesRead} file(s)  "
                + $"[dim]{Markup.Escape(finding.Check.SpecialistId)}[/]");
            output.WriteLine($"  [bold]{Markup.Escape(finding.Check.Rule)}[/]");

            foreach (var (path, count) in finding.Files)
            {
                output.WriteLine($"    {count,4}  {Markup.Escape(path)}");
            }

            // Said with the finding rather than in a footnote. A count whose
            // limits are not stated is one somebody either over-trusts once or
            // stops reading entirely.
            output.WriteLine($"  [dim]{Markup.Escape(finding.Check.Caveat)}[/]");
        }

        output.WriteBlankLine();
        output.WriteLine(
            "[dim]Nothing was changed. These are counts, not verdicts: read them against what "
            + "the code is for.[/]");

        return settings.Strict ? (int)ExitCode.GeneralFailure : CommandOutput.Success();
    }
}

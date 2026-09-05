using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Backups;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Shared plumbing for the memory commands: they all need a project, and they
/// all accept either an explicit handle or the repository the caller is
/// standing in.
/// </summary>
public abstract class MemoryCommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : MemoryCommandBase<TSettings>.MemorySettings
{
    protected MemoryCommandBase(
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

    public class MemorySettings : GlobalSettings
    {
        /// <summary>
        /// How the project is named on the command line, which is not the same
        /// for every memory command.
        /// </summary>
        /// <remarks>
        /// Declared without a position here so that each command can say. Most
        /// take it as their only positional argument. 'write' cannot: it has a
        /// topic to take as well, and an optional argument in front of a
        /// required one is not optional at all — Spectre binds by declared
        /// position, so the first word typed went to the project and the topic
        /// was reported missing however many arguments were given.
        /// </remarks>
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public virtual string? Project { get; init; }
    }

    /// <summary>
    /// Resolves the project by handle, or by the current directory when no
    /// handle was given.
    /// </summary>
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

/// <summary>Lists a project's memory topics.</summary>
[Description("List the durable facts recorded for a project.")]
public sealed class MemoryListCommand : MemoryCommandBase<MemoryListCommand.Settings>
{
    private readonly IMemoryService _memory;

    public MemoryListCommand(
        IMemoryService memory,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
        : base(projects, workspace, console) => _memory = memory;

    public sealed class Settings : MemorySettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public override string? Project { get; init; }

        [CommandOption("--show <TOPIC>")]
        [Description("Print one topic in full instead of listing them.")]
        public string? Show { get; init; }
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

        var listed = await _memory.ListAsync(Workspace.LocalPath, slug.Value!).ConfigureAwait(false);
        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var topics = listed.Value!;

        if (settings.Show is not null)
        {
            var topic = topics.FirstOrDefault(
                t => t.Name.Equals(settings.Show, StringComparison.OrdinalIgnoreCase));

            if (topic is null)
            {
                return output.Fail(
                    $"'{settings.Show}' is not a memory topic of {slug.Value}.",
                    ExitCode.InvalidArguments);
            }

            if (output.IsJson)
            {
                output.WriteJson(new { topic.Name, topic.Description, topic.Facts, topic.Links });
                return CommandOutput.Success();
            }

            output.WriteLine($"[bold]{Markup.Escape(topic.Name)}[/]");

            if (topic.Description.Length > 0)
            {
                output.WriteLine($"[dim]{Markup.Escape(topic.Description)}[/]");
            }

            output.WriteBlankLine();

            foreach (var bullet in topic.Facts)
            {
                output.WriteLine($"  - {Markup.Escape(bullet)}");
            }

            return CommandOutput.Success();
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = slug.Value,
                topics = topics.Select(t => new
                {
                    t.Name,
                    t.Description,
                    kind = t.Kind.ToString().ToLowerInvariant(),
                    facts = t.Facts.Count,
                    t.Bytes,
                }),
            });

            return CommandOutput.Success();
        }

        if (topics.Count == 0)
        {
            output.WriteLine(
                $"[dim]{Markup.Escape(slug.Value!)} has no memory yet. Record a fact with:[/]");
            output.WriteLine("  loadout memory write <topic> --fact \"...\"");
            return CommandOutput.Success();
        }

        var table = new Table().Border(TableBorder.Simple).BorderColor(Color.Grey);
        table.AddColumn("Topic");
        table.AddColumn("Kind");
        table.AddColumn(new TableColumn("Facts").RightAligned());
        table.AddColumn("Description");

        foreach (var topic in topics)
        {
            table.AddRow(
                Markup.Escape(topic.Name),
                topic.Kind.ToString().ToLowerInvariant(),
                topic.Facts.Count.ToString(CultureInfo.InvariantCulture),
                Markup.Escape(topic.Description));
        }

        output.Write(table);

        return CommandOutput.Success();
    }
}

/// <summary>
/// Records a fact.
/// <para>
/// Deliberately awkward to use in bulk. Memory earns its keep by being small
/// and true; a command that made it easy to paste a transcript in would produce
/// something nobody trusts and everybody pays to load.
/// </para>
/// </summary>
[Description("Record a durable fact about a project.")]
public sealed class MemoryWriteCommand : MemoryCommandBase<MemoryWriteCommand.Settings>
{
    private readonly IMemoryService _memory;

    public MemoryWriteCommand(
        IMemoryService memory,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
        : base(projects, workspace, console) => _memory = memory;

    public sealed class Settings : MemorySettings
    {
        [CommandArgument(0, "<topic>")]
        [Description("Topic name, for example 'build-quirks'.")]
        public string Topic { get; init; } = string.Empty;

        // An option rather than a second positional, the way 'memory find'
        // takes it. Behind the topic it would read as though the two were
        // interchangeable, and getting them the wrong way round records the
        // fact under the wrong name without saying so.
        [CommandOption("--project <SLUG>")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public override string? Project { get; init; }

        [CommandOption("--fact <TEXT>")]
        [Description("A fact to record. Repeat for several.")]
        public string[] Facts { get; init; } = [];

        [CommandOption("--description <TEXT>")]
        [Description("One line saying what the topic covers.")]
        public string? Description { get; init; }

        [CommandOption("--kind <KIND>")]
        [Description("project, decision, lesson or reference. Defaults to project.")]
        public string? Kind { get; init; }

        [CommandOption("--scope <SCOPE>")]
        [Description("project (the default), user, or machine for what is only true here.")]
        public string? Scope { get; init; }

        [CommandOption("--separate")]
        [Description("Start a new topic even though existing ones cover similar ground.")]
        public bool Separate { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(Console, settings);

        if (settings.Facts.Length == 0)
        {
            return output.Fail(
                "Nothing to record. Pass at least one --fact.", ExitCode.InvalidArguments);
        }

        var kind = settings.Kind?.ToLowerInvariant() switch
        {
            null or "project" => MemoryKind.Project,
            "decision" => MemoryKind.Decision,
            "lesson" => MemoryKind.Lesson,
            "reference" => MemoryKind.Reference,
            _ => (MemoryKind?)null,
        };

        if (kind is null)
        {
            return output.Fail(
                $"'{settings.Kind}' is not a memory kind. Use project, decision, lesson or reference.",
                ExitCode.InvalidArguments);
        }

        var scope = settings.Scope is null or ""
            ? MemoryScope.Project
            : Enum.TryParse<MemoryScope>(settings.Scope, ignoreCase: true, out var parsedScope)
                ? parsedScope
                : (MemoryScope?)null;

        if (scope is null)
        {
            return output.Fail(
                $"'{settings.Scope}' is not a scope. Use project, user or machine.",
                ExitCode.InvalidArguments);
        }

        var slug = await ResolveSlugAsync(settings).ConfigureAwait(false);
        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        // Said before the write, where it can still be acted on. The same check
        // runs in the audit, but by then the fact is committed and somebody has
        // to go and edit it.
        foreach (var fact in settings.Facts)
        {
            var verdict = MemoryFactClassifier.Classify(fact);

            if (verdict != FactVerdict.Durable)
            {
                output.WriteLine(
                    $"[yellow]Noted, but[/] \"{Markup.Escape(Shorten(fact))}\" "
                    + Markup.Escape(MemoryFactClassifier.Explain(verdict)));
            }
        }

        // Everything above this line reads. Nothing below it does, so the
        // preview goes here: it has the project, the kind, the scope and the
        // verdict on each fact to report, and none of them cost a write.
        //
        // This command wrote the file and added the index line under --dry-run,
        // and said "commit it with: loadout workspace save" while it did — the
        // same shape of defect 'workspace save' had, where the preview and the
        // real run were indistinguishable from the output.
        if (settings.DryRun)
        {
            output.WriteLine(
                $"[bold]Would record[/] {settings.Facts.Length} fact(s) under "
                + $"'{Markup.Escape(settings.Topic)}' for {Markup.Escape(slug.Value!)} "
                + $"as a {kind.Value.ToString().ToLowerInvariant()} memory, "
                + $"scoped to {scope.Value.ToString().ToLowerInvariant()}. Nothing was changed.");

            return CommandOutput.Success();
        }

        var written = await _memory.WriteAsync(
            Workspace.LocalPath,
            slug.Value!,
            settings.Topic,
            settings.Description ?? string.Empty,
            kind.Value,
            settings.Facts,
            settings.Separate,
            scope.Value).ConfigureAwait(false);

        if (written.Failed)
        {
            return output.Fail(written);
        }

        var topic = written.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new { topic.Name, topic.Path, facts = topic.Facts.Count });
            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[green]Recorded[/] {topic.Facts.Count} fact(s) in "
            + $"[bold]{Markup.Escape(topic.Name)}[/].");
        output.WriteLine($"[dim]{Markup.Escape(topic.Path)}[/]");
        output.WriteLine("[dim]It is in the workspace repository; commit it with: "
            + "loadout workspace save[/]");

        return CommandOutput.Success();
    }

    private static string Shorten(string value) =>
        value.Length <= 60 ? value : value[..60] + "...";
}

/// <summary>
/// Checks memory for the things that make it untrustworthy.
/// <para>
/// Memory that nobody audits decays into a pile of half-true statements which
/// still get loaded on every session, so it becomes worse than having none:
/// it costs the same and misleads.
/// </para>
/// </summary>
[Description("Check a project's memory for secrets, duplicates, staleness and index rot.")]
public sealed class MemoryAuditCommand : MemoryCommandBase<MemoryAuditCommand.Settings>
{
    private readonly IMemoryService _memory;
    private readonly IBackupService _backups;

    public MemoryAuditCommand(
        IMemoryService memory,
        IBackupService backups,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
        : base(projects, workspace, console)
    {
        _memory = memory;
        _backups = backups;
    }

    public sealed class Settings : MemorySettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public override string? Project { get; init; }

        [CommandOption("--stale-months <MONTHS>")]
        [Description("Age at which a dated fact is worth rechecking. Defaults to 6.")]
        public int StaleMonths { get; init; } = 6;

        [CommandOption("--strict")]
        [Description("Exit non-zero on warnings as well as errors, for use in CI.")]
        public bool Strict { get; init; }

        [CommandOption("--clean")]
        [Description("Show what mechanical cleanup would remove.")]
        public bool Clean { get; init; }

        [CommandOption("--apply")]
        [Description("With --clean, actually remove it. A backup is taken first.")]
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

        if (settings.Clean)
        {
            return await CleanAsync(output, slug.Value!, settings.Apply).ConfigureAwait(false);
        }

        var audited = await _memory
            .AuditAsync(Workspace.LocalPath, slug.Value!, settings.StaleMonths)
            .ConfigureAwait(false);

        if (audited.Failed)
        {
            return output.Fail(audited);
        }

        var audit = audited.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = audit.Slug,
                verdict = audit.Verdict,
                topics = audit.Topics.Count,
                hasIndex = audit.HasIndex,
                findings = audit.Findings.Select(f => new
                {
                    f.Topic,
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

        // A credential in memory is a failure whether or not anybody asked for
        // strictness: it is committed to a shared repository, and reporting it
        // with a zero exit would let a pipeline sail past it.
        if (audit.Errors.Any())
        {
            return (int)ExitCode.PolicyViolation;
        }

        return settings.Strict && audit.Warnings.Any()
            ? (int)ExitCode.GeneralFailure
            : CommandOutput.Success();
    }

    /// <summary>
    /// Removes what can be removed without judgement, and nothing else.
    /// <para>
    /// The restriction is what makes this safe to run unattended. Empty topics,
    /// word-for-word repeats and index lines pointing nowhere are unambiguous;
    /// everything else the audit finds needs somebody to decide, and a tool that
    /// decided for them would eventually delete the better wording of two
    /// similar facts.
    /// </para>
    /// </summary>
    private async Task<int> CleanAsync(CommandOutput output, string slug, bool apply)
    {
        var previewed = await _memory
            .CleanAsync(Workspace.LocalPath, slug, apply: false)
            .ConfigureAwait(false);

        if (previewed.Failed)
        {
            return output.Fail(previewed);
        }

        var preview = previewed.Value!;

        if (preview.IsEmpty)
        {
            output.WriteLine("[green]Nothing to clean.[/] "
                + "[dim]Everything left needs a person to decide; run without --clean to see it.[/]");

            return CommandOutput.Success();
        }

        RenderCleanup(output, preview);

        if (!apply)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was changed. Add --apply to remove it.[/]");

            return CommandOutput.Success();
        }

        var captured = await _backups.CaptureAsync(
            "memory clean",
            slug,
            _memory.CleanupPaths(Workspace.LocalPath, slug)).ConfigureAwait(false);

        if (captured.Failed)
        {
            return output.Fail(
                "The cleanup was not started because a backup could not be taken: "
                + captured.Error,
                captured.ExitCode);
        }

        var cleaned = await _memory
            .CleanAsync(Workspace.LocalPath, slug, apply: true)
            .ConfigureAwait(false);

        if (cleaned.Failed)
        {
            return output.Fail(cleaned);
        }

        output.WriteBlankLine();
        output.WriteLine($"[green]Removed[/] {cleaned.Value!.Count} item(s).");
        output.WriteLine(
            $"[dim]Undo it with:[/] loadout backup restore {Markup.Escape(captured.Value!.Id)}");

        return CommandOutput.Success();
    }

    private static void RenderCleanup(CommandOutput output, MemoryCleanup cleanup)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                applied = cleanup.Applied,
                removedTopics = cleanup.RemovedTopics,
                removedBullets = cleanup.RemovedBullets,
                removedIndexLines = cleanup.RemovedIndexLines,
            });

            return;
        }

        foreach (var topic in cleanup.RemovedTopics)
        {
            output.WriteLine($"  [yellow]topic[/]      {Markup.Escape(topic)}  [dim]holds no facts[/]");
        }

        foreach (var bullet in cleanup.RemovedBullets)
        {
            output.WriteLine($"  [yellow]duplicate[/]  {Markup.Escape(bullet)}");
        }

        foreach (var line in cleanup.RemovedIndexLines)
        {
            output.WriteLine($"  [yellow]index[/]      {Markup.Escape(line)}  [dim]target is gone[/]");
        }
    }

    private static void Render(CommandOutput output, MemoryAudit audit)
    {
        output.WriteLine(
            $"[bold]{Markup.Escape(audit.Slug)}[/]  [dim]{audit.Topics.Count} topic(s), "
            + $"{audit.Topics.Sum(t => t.Facts.Count)} fact(s)[/]");

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
                    MemoryFindingSeverity.Error => "red",
                    MemoryFindingSeverity.Warning => "yellow",
                    _ => "grey",
                };

                var where = finding.Topic is null
                    ? string.Empty
                    : Markup.Escape(finding.Topic) + " ";

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

/// <summary>Rewrites the memory index from the topics on disk.</summary>
[Description("Rebuild MEMORY.md from the topic files that actually exist.")]
public sealed class MemoryReindexCommand : MemoryCommandBase<MemoryReindexCommand.Settings>
{
    private readonly IMemoryService _memory;

    public MemoryReindexCommand(
        IMemoryService memory,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
        : base(projects, workspace, console) => _memory = memory;

    public sealed class Settings : MemorySettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public override string? Project { get; init; }
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

        var rebuilt = await _memory
            .RebuildIndexAsync(Workspace.LocalPath, slug.Value!)
            .ConfigureAwait(false);
        if (rebuilt.Failed)
        {
            return output.Fail(rebuilt);
        }

        output.WriteLine($"[green]Index rebuilt[/] for {Markup.Escape(slug.Value!)}.");

        return CommandOutput.Success();
    }
}

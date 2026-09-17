using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Reports where a repository's documentation has come adrift from it.
/// </summary>
/// <remarks>
/// <para>
/// Loadout already does this for itself, by hand, three times over: a test that
/// every command the documentation names exists, one that the install examples
/// name the version that ships, and one that the specialist count is the count.
/// Each was written after the drift it now catches — a table left naming the old
/// sub-commands, a count left at 71, a download link left at 0.9.2 through five
/// releases. This is that habit offered to every project instead of to this one.
/// </para>
/// <para>
/// Read-only. It reports and changes nothing, which is the same posture the
/// convention auditor takes and for the same reason: what to do about a stale
/// page is a judgement about a codebase.
/// </para>
/// </remarks>
[Description("Report where the documentation has come adrift from the repository.")]
[CommandMeta(CommandCategory.Health, Intent = "documentation docs stale links broken references audit")]
public sealed class DocsAuditCommand : AsyncCommand<DocsAuditCommand.Settings>
{
    /// <summary>Where documentation lives when nobody has said otherwise.</summary>
    private static readonly string[] DefaultRoots = ["docs", "."];

    private readonly IProjectService _projects;
    private readonly Loadout.Core.Workspace.IWorkspaceManager _workspace;
    private readonly Loadout.Core.Configuration.YamlStore _yaml;
    private readonly IAnsiConsole _console;

    public DocsAuditCommand(
        IProjectService projects,
        Loadout.Core.Workspace.IWorkspaceManager workspace,
        Loadout.Core.Configuration.YamlStore yaml,
        IAnsiConsole console)
    {
        _projects = projects;
        _workspace = workspace;
        _yaml = yaml;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public string? Project { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = settings.Project is { Length: > 0 } handle
            ? await _projects.ResolveAsync(handle, cancellationToken).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);

        var repository = resolution.Succeeded
            ? resolution.Value!.LocalPath
            : settings.Repo ?? Directory.GetCurrentDirectory();

        if (repository is null || !Directory.Exists(repository))
        {
            return output.Fail(
                "There is no repository here to audit. Name a project, or run this inside one.",
                ExitCode.RepositoryUnavailable);
        }

        var documents = Documents(repository);

        if (documents.Count == 0)
        {
            output.WriteLine("[yellow]No Markdown documentation found to audit.[/]");

            return CommandOutput.Success();
        }

        // From the workspace rather than the repository, so a project keeps its
        // source and the rules about its source in different places. A project
        // with no policy still gets every check that needs no configuration.
        var policy = resolution.Succeeded
            ? await PolicyAsync(resolution.Value!.Entry.Slug, cancellationToken).ConfigureAwait(false)
            : null;

        var findings = DocsAuditor.Audit(repository, documents, policy, cancellationToken);

        if (output.IsJson)
        {
            output.WriteJson(new { documents = documents.Count, findings });

            return CommandOutput.Success();
        }

        if (findings.Count == 0)
        {
            output.WriteLine(
                $"[green]+[/] {documents.Count} document(s), every reference resolves.");

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{documents.Count}[/] document(s), "
            + $"[bold]{findings.Count}[/] finding(s)");
        output.WriteBlankLine();

        foreach (var group in findings.GroupBy(finding => finding.Path).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"[bold]{Markup.Escape(group.Key)}[/]");

            foreach (var finding in group.OrderBy(f => f.Line))
            {
                var where = finding.Line > 0 ? $":{finding.Line}" : string.Empty;

                output.WriteLine(
                    $"  [dim]{finding.Kind}{where}[/]  {Markup.Escape(finding.Detail)}");
            }

            output.WriteBlankLine();
        }

        // Reported, not failed. A stale reference is worth knowing about and is
        // not a reason for a command to exit non-zero in somebody's pipeline.
        return CommandOutput.Success();
    }

    /// <summary>
    /// The project's documentation policy, or null when it has none.
    /// </summary>
    /// <remarks>
    /// A missing file is the ordinary case and reads as no policy rather than a
    /// failure: most projects have nothing that needs counting, and the checks
    /// that need no configuration are the ones most projects want.
    /// </remarks>
    private async Task<DocsPolicy?> PolicyAsync(string slug, CancellationToken ct)
    {
        if (!_workspace.IsAvailable())
        {
            return null;
        }

        var path = Path.Combine(_workspace.LocalPath, "projects", slug, "docs.yaml");

        if (!File.Exists(path))
        {
            return null;
        }

        var loaded = await _yaml.LoadAsync(path, () => new DocsPolicy(), ct).ConfigureAwait(false);

        return loaded.Succeeded ? loaded.Value : null;
    }

    /// <summary>
    /// The Markdown worth auditing: the docs directory, and the pages beside the
    /// root README rather than every Markdown file in the tree.
    /// </summary>
    /// <remarks>
    /// A repository holds Markdown that is not documentation — issue templates,
    /// a licence, notes inside a package nobody here wrote. Walking everything
    /// would turn a report about the documentation into a report about the
    /// repository.
    /// </remarks>
    private static IReadOnlyList<string> Documents(string repository)
    {
        var found = new List<string>();

        foreach (var root in DefaultRoots)
        {
            var directory = Path.Combine(repository, root);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            var search = root == "."
                ? SearchOption.TopDirectoryOnly
                : SearchOption.AllDirectories;

            foreach (var file in Directory.EnumerateFiles(directory, "*.md", search))
            {
                var relative = Path.GetRelativePath(repository, file).Replace('\\', '/');

                if (!found.Contains(relative, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(relative);
                }
            }
        }

        return found.Order(StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// Writes one of four documents from a scan of the code.
/// </summary>
/// <remarks>
/// The four are not equally derivable, and the command says so where it
/// matters. The reference and the machine index fall out of the source and need
/// nobody; the technical guide is the prose already in the doc comments,
/// arranged; the user guide is a scaffold, because what somebody wants to do is
/// not in the source and generating it anyway produces something that reads
/// like documentation and teaches nothing.
/// </remarks>
[Description("Write a reference, technical guide, user-guide scaffold or machine index.")]
[CommandMeta(CommandCategory.Health,
    Intent = "documentation generate export reference api guide index llms", Mutates = true)]
public sealed class DocsExportCommand : AsyncCommand<DocsExportCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly ISymbolIndexService _symbols;
    private readonly IAnsiConsole _console;

    public DocsExportCommand(IProjectService projects, ISymbolIndexService symbols, IAnsiConsole console)
    {
        _projects = projects;
        _symbols = symbols;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--type <TYPE>")]
        [Description("reference, technical, user-guide or machine-index. Defaults to reference.")]
        public string Type { get; init; } = "reference";

        [CommandOption("--out <PATH>")]
        [Description("Where to write it. Prints to standard output when omitted.")]
        public string? Out { get; init; }

        [CommandOption("--project <SLUG>")]
        [Description("Project to document. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--front-matter")]
        [Description("Prefix the YAML header Docusaurus and MkDocs read.")]
        public bool FrontMatter { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (!TryReadType(settings.Type, out var type))
        {
            return output.Fail(
                $"'{settings.Type}' is not a document type. Use reference, technical, "
                + "user-guide or machine-index.",
                ExitCode.InvalidArguments);
        }

        var resolution = settings.Project is { Length: > 0 } handle
            ? await _projects.ResolveAsync(handle, cancellationToken).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), cancellationToken)
                .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var project = resolution.Value!;

        if (project.LocalPath is not { Length: > 0 } path || !Directory.Exists(path))
        {
            return output.Fail(
                $"'{project.Entry.Slug}' is not on this machine, so there is nothing to read.",
                ExitCode.RepositoryUnavailable);
        }

        // The same scan the index makes: git's file list, the project's own
        // exclusions and mappings, and the tagger for what the table cannot
        // read. Two scans that disagreed would make the documents and the
        // lookup describe different codebases.
        var symbols = await _symbols.ScanAsync(path, project.Entry.Slug, cancellationToken)
            .ConfigureAwait(false);

        if (symbols.Count == 0)
        {
            return output.Fail(
                $"Nothing was found to document under {path}.", ExitCode.GeneralFailure);
        }

        var document = DocsExport.Write(
            type, symbols, project.Entry.Name, settings.FrontMatter);

        if (settings.Out is not { Length: > 0 } destination)
        {
            System.Console.Out.Write(document);

            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would write {symbols.Count} symbol(s) as {type.ToString().ToLowerInvariant()} "
                + $"to {Markup.Escape(destination)}. Nothing was written.");

            return CommandOutput.Success();
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(destination));

            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(destination, document, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return output.Fail(
                $"Could not write '{destination}': {exception.Message}", ExitCode.GeneralFailure);
        }

        output.WriteLine(
            $"[green]+[/] Wrote {symbols.Count} symbol(s) to {Markup.Escape(destination)}.");

        if (type == DocsExportType.UserGuide)
        {
            // Said here as well as in the file. Somebody who ran the command
            // and did not open the output is exactly the person who would
            // otherwise publish a scaffold.
            output.WriteLine(
                "[yellow]note[/] that is a scaffold, not a guide. The headings come from the "
                + "shape of the code; what a reader wants to do is not in the source.");
        }

        return CommandOutput.Success();
    }

    internal static bool TryReadType(string? given, out DocsExportType type)
    {
        switch (given?.Trim().ToLowerInvariant())
        {
            case null or "" or "reference": type = DocsExportType.Reference; return true;
            case "technical" or "technical-guide": type = DocsExportType.Technical; return true;
            case "user-guide" or "user" or "guide": type = DocsExportType.UserGuide; return true;
            case "machine-index" or "machine" or "index": type = DocsExportType.MachineIndex; return true;
            default: type = DocsExportType.Reference; return false;
        }
    }
}

/// <summary>
/// Which tree a symbol command should read.
/// </summary>
/// <remarks>
/// A project resolved from a directory carries the registered checkout's
/// path, and from a linked worktree or a second clone that is a different
/// tree on a different branch from the one being edited. So the tree the
/// caller is standing in wins when it is one, and the registered path is
/// what a project named outright gets — <c>--project</c> says "that one",
/// wherever the command was typed.
/// </remarks>
internal static class SymbolTree
{
    /// <summary>The tree to read, or null when the project is not on this machine.</summary>
    /// <param name="project">The resolved project.</param>
    /// <param name="directory">Where the caller is, when that should be preferred.</param>
    public static string? Of(ProjectResolution project, string? directory)
    {
        if (directory is { Length: > 0 }
            && Loadout.Core.Git.GitHead.WorkingTreeRoot(directory) is { } root
            && Directory.Exists(root))
        {
            return root;
        }

        return project.LocalPath is { Length: > 0 } path && Directory.Exists(path) ? path : null;
    }
}

/// <summary>
/// Says where a type or member is declared.
/// </summary>
/// <remarks>
/// <para>
/// The question an agent asks most and answers most expensively. "Where is
/// <c>PreflightService</c>" is a search across the tree, paid in tokens for the
/// listing and again for whatever it opened on the way; this answers it with
/// one line from an index kept in step with the repository. The same lookup is
/// served to an agent as <c>loadout_locate</c>, through the same service, so
/// the two cannot drift.
/// </para>
/// <para>
/// Names, not meanings. It finds a name, whole or in part, and nothing about
/// what the thing does — the memory index is for that. The language comes
/// from each file's extension; one the scan does not read gets an honest
/// nothing rather than a guess.
/// </para>
/// </remarks>
[Description("Say where a type or member is declared, from an index kept current with the repository.")]
[CommandMeta(CommandCategory.Health,
    Intent = "find symbol where is declared locate definition type member class lookup")]
public sealed class DocsFindCommand : AsyncCommand<DocsFindCommand.Settings>
{
    private readonly ISymbolIndexService _symbols;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public DocsFindCommand(
        ISymbolIndexService symbols,
        IProjectService projects,
        IAnsiConsole console)
    {
        _symbols = symbols;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("A type or member name, whole or in part. Case does not matter.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--project <SLUG>")]
        [Description("Project to look in. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--limit <COUNT>")]
        [Description("How many to show. Defaults to 10.")]
        public int Limit { get; init; } = 10;

        [CommandOption("--rescan")]
        [Description("Read the tree again rather than the cached index.")]
        public bool Rescan { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Limit <= 0)
        {
            return output.Fail("--limit has to be at least 1.", ExitCode.InvalidArguments);
        }

        var resolution = settings.Project is { Length: > 0 } handle
            ? await _projects.ResolveAsync(handle, cancellationToken).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), cancellationToken)
                .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var project = resolution.Value!;

        var path = SymbolTree.Of(
            project,
            settings.Project is { Length: > 0 } ? null : settings.Repo ?? Directory.GetCurrentDirectory());

        if (path is null)
        {
            return output.Fail(
                $"'{project.Entry.Slug}' is not on this machine, so there is nothing to look in.",
                ExitCode.RepositoryUnavailable);
        }

        var found = await _symbols
            .FindAsync(path, project.Entry.Slug, settings.Name, settings.Limit, settings.Rescan,
                cancellationToken)
            .ConfigureAwait(false);

        if (found.Failed)
        {
            return output.Fail(found);
        }

        var lookup = found.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                lookup.Indexed,
                lookup.Head,
                lookup.FromCache,
                lookup.Rebuilt,
                Matches = lookup.Matches.Select(match => new
                {
                    match.Symbol.Name,
                    Kind = match.Symbol.Kind.ToString().ToLowerInvariant(),
                    match.Symbol.File,
                    match.Symbol.Line,
                    match.Symbol.Summary,
                    match.Exact,
                }),
            });

            return CommandOutput.Success();
        }

        if (lookup.Matches.Count == 0)
        {
            output.WriteLine(
                $"[yellow]Nothing named like '{Markup.Escape(settings.Name)}' in "
                + $"{Markup.Escape(project.Entry.Slug)}.[/]");

            // Said because the alternative is an agent concluding the thing
            // does not exist. It matches names, whole or in part, in the
            // languages it reads; a thing called something else, or written
            // in something else, is not found rather than absent.
            output.WriteLine(
                "[dim]It matches names rather than meanings. Try a shorter fragment. "
                + $"{lookup.Indexed} symbol(s) were checked{Provenance(lookup)}, reading "
                + $"{Markup.Escape(SymbolLanguages.Names)}.[/]");

            return CommandOutput.Success();
        }

        foreach (var match in lookup.Matches)
        {
            var summary = match.Symbol.Summary.Length > 0
                ? $"  [dim]{Markup.Escape(match.Symbol.Summary)}[/]"
                : string.Empty;

            output.WriteLine(
                $"{Markup.Escape(match.Symbol.File)}:{match.Symbol.Line}  "
                + $"[dim]{match.Symbol.Kind.ToString().ToLowerInvariant()}[/]  "
                + $"[bold]{Markup.Escape(match.Symbol.Name)}[/]{summary}");
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[dim]{lookup.Matches.Count} of {lookup.Indexed} symbol(s){Provenance(lookup)}.[/]");

        return CommandOutput.Success();
    }

    /// <summary>Where the index came from, so a stale answer can be recognised as one.</summary>
    private static string Provenance(SymbolLookup lookup)
    {
        var at = lookup.Head is { Length: >= 7 } head ? $" at {head[..7]}" : string.Empty;

        return lookup switch
        {
            { Rebuilt: true } => $", scanned again{at}",
            { FromCache: true } => $", from the index{at}",
            _ => $", scanned now{at}",
        };
    }
}

/// <summary>
/// Brings the cached index up to date for the files named.
/// </summary>
/// <remarks>
/// <para>
/// Made to be run by a hook after every edit, so it costs what one file
/// costs. The index behind <c>docs find</c> is keyed by commit, and a session's
/// edits do not move the commit; a lookup already re-reads any file it names,
/// but the first lookup for a name added this session pays a full rescan to
/// find it, and the map of directories is not corrected at all. This corrects
/// both, one file at a time, as the edits happen.
/// </para>
/// <para>
/// Writes to the machine's cache and nowhere else. It still honours
/// <c>--dry-run</c>, because a command that changes something should say what
/// it would change when asked.
/// </para>
/// </remarks>
[Description("Bring the symbol index up to date for the files named, after an edit.")]
[CommandMeta(CommandCategory.Health,
    Intent = "refresh symbol index update after edit hook rescan file", Mutates = true)]
public sealed class DocsRefreshCommand : AsyncCommand<DocsRefreshCommand.Settings>
{
    private readonly ISymbolIndexService _symbols;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public DocsRefreshCommand(
        ISymbolIndexService symbols,
        IProjectService projects,
        IAnsiConsole console)
    {
        _symbols = symbols;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[file]")]
        [Description("Files that changed, absolute or relative to the repository.")]
        public string[] Files { get; init; } = [];

        [CommandOption("--project <SLUG>")]
        [Description("Project the files belong to. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--hook")]
        [Description(
            "Run as an agent's after-edit hook: read the files from the hook payload on standard "
            + "input, and write a line for the agent only when the map of the code changed.")]
        public bool Hook { get; init; }

        [CommandOption("--dialect <NAME>")]
        [Description(
            "Whose hook this is: claude (the default) writes the JSON document Claude Code reads "
            + "back; generic writes plain text, for a hook that shows or ignores it.")]
        public string Dialect { get; init; } = "claude";
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Hook)
        {
            return await AsHookAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        var output = new CommandOutput(_console, settings);

        if (settings.Files.Length == 0)
        {
            return output.Fail("Name at least one file that changed.", ExitCode.InvalidArguments);
        }

        var resolution = await ResolveAsync(settings, settings.Repo, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var project = resolution.Value!;

        var path = SymbolTree.Of(
            project,
            settings.Project is { Length: > 0 } ? null : settings.Repo ?? Directory.GetCurrentDirectory());

        if (path is null)
        {
            return output.Fail(
                $"'{project.Entry.Slug}' is not on this machine, so there is no index to refresh.",
                ExitCode.RepositoryUnavailable);
        }

        var refreshed = await _symbols
            .RefreshAsync(path, project.Entry.Slug, settings.Files, settings.DryRun, cancellationToken)
            .ConfigureAwait(false);

        if (refreshed.Failed)
        {
            return output.Fail(refreshed);
        }

        var result = refreshed.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                result.Indexed,
                result.Head,
                result.Built,
                DryRun = settings.DryRun,
                result.Files,
                MapChanges = result.Changes,
            });

            return CommandOutput.Success();
        }

        var would = settings.DryRun ? "Would " : string.Empty;

        if (result.Head is null)
        {
            output.WriteLine(
                "[dim]Not a repository, so there is no index to keep current: every lookup "
                + "here reads the tree.[/]");

            return CommandOutput.Success();
        }

        if (result.Built)
        {
            output.WriteLine(
                $"{would}{(settings.DryRun ? "build" : "Built")} the index for "
                + $"{Markup.Escape(project.Entry.Slug)}: {result.Indexed} symbol(s) at "
                + $"{result.Head[..Math.Min(7, result.Head.Length)]}. There was none to refresh.");

            return CommandOutput.Success();
        }

        foreach (var file in result.Files)
        {
            var change = file.After == file.Before
                ? $"{file.After} symbol(s), unchanged in number"
                : $"{file.After} symbol(s), was {file.Before}";

            output.WriteLine($"{would}{Markup.Escape(file.File)}: {change}");
        }

        foreach (var change in result.Changes)
        {
            output.WriteLine(
                change.After is null
                    ? $"[bold]map[/] `{Markup.Escape(change.Directory)}` no longer holds any types"
                    : $"[bold]map[/] {Markup.Escape(change.After)}");
        }

        output.WriteLine(
            $"[dim]{result.Indexed} symbol(s) in the index at "
            + $"{result.Head[..Math.Min(7, result.Head.Length)]}"
            + (settings.DryRun ? ". Nothing was written." : ".") + "[/]");

        return CommandOutput.Success();
    }

    /// <summary>
    /// The hook form: everything comes from stdin, and only a change to the map
    /// goes to stdout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Claude reads stdout as a document, so nothing may be written there but
    /// the one document or nothing. Every failure is therefore reported on
    /// stderr and the exit code is success: a refresh that could not happen
    /// costs the next lookup a rescan, which is a far smaller thing than an
    /// error in front of the agent after every edit.
    /// </para>
    /// <para>
    /// The payload names the directory the agent is in, which is the project
    /// unless <c>--project</c> says otherwise.
    /// </para>
    /// </remarks>
    private async Task<int> AsHookAsync(Settings settings, CancellationToken cancellationToken)
    {
        var payload = await System.Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var input = RefreshHook.Parse(payload);

        if (input.Files.Count == 0)
        {
            return CommandOutput.Success();
        }

        var dialect = settings.Dialect.Trim().ToLowerInvariant() switch
        {
            "" or "claude" => HookDialect.Claude,
            _ => HookDialect.Generic,
        };

        var resolution = await ResolveAsync(settings, input.WorkingDirectory ?? settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        // The tree the agent is in, whatever the project is registered as:
        // a session launched into a worktree edits the worktree.
        var path = resolution.Succeeded
            ? SymbolTree.Of(resolution.Value!, input.WorkingDirectory ?? settings.Repo)
            : null;

        if (path is null)
        {
            await System.Console.Error.WriteLineAsync(
                "loadout docs refresh: no project could be worked out from here, so the index was "
                + "not refreshed.").ConfigureAwait(false);

            return CommandOutput.Success();
        }

        var refreshed = await _symbols
            .RefreshAsync(path, resolution.Value!.Entry.Slug, input.Files, dryRun: false, cancellationToken)
            .ConfigureAwait(false);

        if (refreshed.Failed)
        {
            await System.Console.Error.WriteLineAsync("loadout docs refresh: " + refreshed.Error)
                .ConfigureAwait(false);

            return CommandOutput.Success();
        }

        if (RefreshHook.Output(refreshed.Value!, dialect) is { } document)
        {
            await System.Console.Out.WriteAsync(document).ConfigureAwait(false);
        }

        return CommandOutput.Success();
    }

    private Task<OperationResult<ProjectResolution>> ResolveAsync(
        Settings settings,
        string? directory,
        CancellationToken cancellationToken) =>
        settings.Project is { Length: > 0 } handle
            ? _projects.ResolveAsync(handle, cancellationToken)
            : _projects.ResolveFromDirectoryAsync(
                directory ?? Directory.GetCurrentDirectory(), cancellationToken);
}

/// <summary>
/// Writes a workflow that regenerates the documents, as a starting point.
/// </summary>
/// <remarks>
/// A starting point and it says so in its own first line. A workflow file
/// dates — action versions move, runner images change — and none of that is
/// this project's to track. The skill beside it is what adapts this to whatever
/// CI a repository actually has, including the ones this cannot write.
/// </remarks>
[Description("Write a CI workflow that regenerates the documents. A starting point, not a fixture.")]
[CommandMeta(CommandCategory.Health,
    Intent = "documentation ci workflow pipeline github actions publish docusaurus", Mutates = true)]
public sealed class DocsCiCommand : AsyncCommand<DocsCiCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public DocsCiCommand(IProjectService projects, IAnsiConsole console)
    {
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--out <PATH>")]
        [Description("Where to write it. Prints to standard output when omitted.")]
        public string? Out { get; init; }

        [CommandOption("--project <SLUG>")]
        [Description("Project to document. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--docs-dir <PATH>")]
        [Description("Where the documents should land in the repository. Defaults to docs/generated.")]
        public string DocsDirectory { get; init; } = "docs/generated";

        [CommandOption("--no-front-matter")]
        [Description("Leave off the YAML header Docusaurus and MkDocs read.")]
        public bool NoFrontMatter { get; init; }

        [CommandOption("--include-user-guide")]
        [Description("Publish the user-guide scaffold too. Only after you have read it.")]
        public bool IncludeUserGuide { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = settings.Project is { Length: > 0 } handle
            ? await _projects.ResolveAsync(handle, cancellationToken).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), cancellationToken)
                .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var workflow = DocsWorkflow.GitHubActions(
            resolution.Value!.Entry.Slug,
            settings.DocsDirectory,
            !settings.NoFrontMatter,
            settings.IncludeUserGuide);

        if (settings.Out is not { Length: > 0 } destination)
        {
            System.Console.Out.Write(workflow);

            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would write a workflow to {Markup.Escape(destination)}. Nothing was written.");

            return CommandOutput.Success();
        }

        if (File.Exists(destination))
        {
            // Refused rather than overwritten. Whatever is there has been
            // adapted to a repository this knows nothing about, and replacing
            // it with a fresh starting point would throw that away.
            return output.Fail(
                $"'{destination}' already exists. This never overwrites a workflow: "
                + "move yours aside if you want a fresh one to compare against.",
                ExitCode.InvalidArguments);
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(destination));

            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(destination, workflow, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return output.Fail(
                $"Could not write '{destination}': {exception.Message}", ExitCode.GeneralFailure);
        }

        output.WriteLine($"[green]+[/] Wrote {Markup.Escape(destination)}.");
        output.WriteLine(
            "[dim]A starting point, not a fixture: it assumes Loadout is on PATH and the "
            + "project registered, and it commits nothing. Adapt it.[/]");

        if (settings.IncludeUserGuide)
        {
            output.WriteLine(
                "[yellow]note[/] the user guide is a scaffold. Publishing it on every push "
                + "gives readers something that reads like documentation and teaches nothing.");
        }

        return CommandOutput.Success();
    }
}

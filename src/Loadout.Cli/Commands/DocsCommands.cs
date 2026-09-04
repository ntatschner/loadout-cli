using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Instructions;
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
    private readonly IAnsiConsole _console;

    public DocsExportCommand(IProjectService projects, IAnsiConsole console)
    {
        _projects = projects;
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

        var symbols = SymbolScan.Scan(path, cancellationToken);

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

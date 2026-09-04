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

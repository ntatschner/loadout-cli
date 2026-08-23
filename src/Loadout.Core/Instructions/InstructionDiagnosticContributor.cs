using Loadout.Core.Diagnostics;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models.Diagnostics;

namespace Loadout.Core.Instructions;

/// <summary>
/// Folds the health of the instruction layer into <c>loadout doctor</c>.
/// <para>
/// The dedicated commands report far more, but somebody has to know they exist
/// to run them. Doctor is where people look when something feels wrong, so the
/// two failures that make an agent behave inexplicably belong here: a
/// credential sitting in memory, and instruction files that have grown large
/// enough to crowd out the task.
/// </para>
/// </summary>
public sealed class InstructionDiagnosticContributor : IDiagnosticContributor
{
    private const string Category = "Instructions";

    /// <summary>The point past which the always-loaded layer is worth a second look.</summary>
    private const long ComfortableAlwaysLoadedBytes = 20 * 1024;

    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IRuleService _rules;
    private readonly IMemoryService _memory;
    private readonly IMemoryImporter _importer;

    public InstructionDiagnosticContributor(
        IProjectService projects,
        IWorkspaceManager workspace,
        IRuleService rules,
        IMemoryService memory,
        IMemoryImporter importer)
    {
        _projects = projects;
        _workspace = workspace;
        _rules = rules;
        _memory = memory;
        _importer = importer;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticCheck>> ContributeAsync(CancellationToken ct = default)
    {
        if (!_workspace.IsAvailable())
        {
            return [];
        }

        var listed = await _projects.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed || listed.Value!.Count == 0)
        {
            return [];
        }

        var checks = new List<DiagnosticCheck>();
        var heavy = new List<string>();
        var leaking = new List<string>();
        var elsewhere = new List<string>();

        foreach (var project in listed.Value)
        {
            ct.ThrowIfCancellationRequested();

            var slug = project.Entry.Slug;

            var rules = await _rules.LoadAsync(_workspace.LocalPath, slug, ct).ConfigureAwait(false);

            if (rules.Succeeded)
            {
                var alwaysLoaded = rules.Value!
                    .Where(r => r.AlwaysApply || r.IsUnscoped)
                    .Sum(r => r.Bytes);

                if (alwaysLoaded > ComfortableAlwaysLoadedBytes)
                {
                    heavy.Add($"{slug} ({alwaysLoaded / 1024}KB)");
                }
            }

            var audit = await _memory.AuditAsync(_workspace.LocalPath, slug, ct: ct)
                .ConfigureAwait(false);

            if (audit.Succeeded && audit.Value!.Errors.Any())
            {
                leaking.Add(slug);
            }

            // Memory an agent recorded before this launcher existed sits in a
            // machine-local directory nothing here reads. Reported because
            // otherwise adopting the launcher looks like starting from nothing
            // on exactly the projects that had accumulated the most, and
            // nobody goes looking for a command they have not heard of.
            if (project.LocalPath is not null
                && _importer.Discover(project.LocalPath) is { } source)
            {
                // Asked rather than inferred from the workspace being empty. A
                // project that has already imported some of it, or written its
                // own, would otherwise never be told about the rest.
                var pending = await _importer
                    .ImportAsync(_workspace.LocalPath, slug, source, apply: false, ct)
                    .ConfigureAwait(false);

                if (pending.Succeeded && pending.Value!.Imported.Count > 0)
                {
                    elsewhere.Add($"{slug} ({pending.Value.Imported.Count})");
                }
            }
        }

        if (leaking.Count > 0)
        {
            // Named as an error, and deliberately without saying which pattern
            // matched or where: the point is to send somebody to the audit, not
            // to reprint the finding in a second place.
            checks.Add(DiagnosticCheck.Error(
                Category,
                "Memory content",
                $"Memory for {string.Join(", ", leaking)} contains something shaped like a "
                + "credential. Run: loadout memory audit <project>"));
        }
        else
        {
            checks.Add(DiagnosticCheck.Ok(
                Category, "Memory content", "No credential-shaped content in project memory."));
        }

        if (elsewhere.Count > 0)
        {
            checks.Add(DiagnosticCheck.Warn(
                Category,
                "Memory outside the workspace",
                $"{string.Join(", ", elsewhere)} has memory recorded by an agent on this machine "
                + "that the workspace does not hold, with the number of topics in brackets. Bring it in with: "
                + "loadout memory import <project>"));
        }

        checks.Add(heavy.Count > 0
            ? DiagnosticCheck.Warn(
                Category,
                "Instruction budget",
                $"Loaded on every session regardless of the task: {string.Join(", ", heavy)}. "
                + "Run: loadout rules budget <project>")
            : DiagnosticCheck.Ok(
                Category, "Instruction budget", "No project loads an oversized instruction layer."));

        return checks;
    }
}

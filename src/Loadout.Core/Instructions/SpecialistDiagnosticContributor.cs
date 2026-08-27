using Loadout.Core.Diagnostics;
using Loadout.Core.Workspace;
using Loadout.Models.Diagnostics;
using Loadout.Models.Instructions;

namespace Loadout.Core.Instructions;

/// <summary>
/// Reports a broken specialist library through the doctor.
/// </summary>
/// <remarks>
/// <para>
/// Through the existing contributor seam rather than as a check of its own, so
/// a specialist problem appears wherever every other problem appears. A
/// diagnostic that exists in only one surface is one nobody finds: the whole
/// point of <c>loadout doctor</c> is that it is the single place to look.
/// </para>
/// <para>
/// Says so when the library is sound, because every other check in the report
/// does. A section that goes quiet on success is indistinguishable from one
/// that did not run.
/// </para>
/// </remarks>
public sealed class SpecialistDiagnosticContributor : IDiagnosticContributor
{
    private const string Category = "Instructions";

    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;

    public SpecialistDiagnosticContributor(
        ISpecialistLibrary library,
        IWorkspaceManager workspace)
    {
        _library = library;
        _workspace = workspace;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticCheck>> ContributeAsync(CancellationToken ct = default)
    {
        var checks = new List<DiagnosticCheck>();

        SpecialistCatalogue catalogue;

        try
        {
            catalogue = await _library
                .LoadAsync(_workspace.IsAvailable() ? _workspace.LocalPath : null, null, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The contract is that a contributor never throws: a doctor that
            // fell over would take every other check down with it.
            return
            [
                new DiagnosticCheck(
                    Category,
                    "Specialist library",
                    DiagnosticSeverity.Error,
                    $"The specialist library could not be read: {ex.Message}"),
            ];
        }

        foreach (var finding in catalogue.Findings)
        {
            checks.Add(new DiagnosticCheck(
                Category,
                finding.Rule is { Length: > 0 } named ? $"Specialist {named}" : "Specialist library",
                finding.Severity switch
                {
                    RuleFindingSeverity.Error => DiagnosticSeverity.Error,
                    RuleFindingSeverity.Warning => DiagnosticSeverity.Warning,
                    _ => DiagnosticSeverity.Info,
                },
                finding.Detail));
        }

        if (checks.Count == 0 && catalogue.Specialists.Count > 0)
        {
            // Every other check in the report says so when it passes, and being
            // the one section that goes quiet on success reads as a section
            // that did not run.
            checks.Add(new DiagnosticCheck(
                Category,
                "Specialist library",
                DiagnosticSeverity.Info,
                $"{catalogue.Specialists.Count} specialists loaded and valid."));
        }

        if (catalogue.Specialists.Count == 0)
        {
            // Nothing at all means the shipped library failed to load, which is
            // a packaging fault rather than a configuration one, and would
            // otherwise show up only as agents quietly receiving no guidance.
            checks.Add(new DiagnosticCheck(
                Category,
                "Specialist library",
                DiagnosticSeverity.Error,
                "No specialists loaded at all. The built-in library is missing from this build."));
        }

        return checks;
    }
}

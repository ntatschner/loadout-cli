using Loadout.Core.Configuration;
using Loadout.Core.Diagnostics;
using Loadout.Core.Projects;
using Loadout.Models.Diagnostics;

namespace Loadout.Core.Editors;

/// <summary>
/// Reports whether the editor is here, and whether the profiles projects ask
/// for actually exist.
/// <para>
/// Configuring a project to open under a profile that was renamed, or that only
/// ever existed on another machine, is silent until somebody opens it and finds
/// an empty editor. It is worth saying so up front.
/// </para>
/// <para>
/// Nothing here is reported as an error. A missing editor is a preference not a
/// dependency — Loadout launches agents perfectly well without one — and a
/// profile that cannot be found is a warning because the editor makes one of
/// that name rather than refusing to open.
/// </para>
/// </summary>
internal sealed class EditorDiagnosticContributor : IDiagnosticContributor
{
    private const string Category = "Editor";

    private readonly IEditorService _editors;
    private readonly IConfigurationService _configuration;
    private readonly IProjectService _projects;

    public EditorDiagnosticContributor(
        IEditorService editors,
        IConfigurationService configuration,
        IProjectService projects)
    {
        _editors = editors;
        _configuration = configuration;
        _projects = projects;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticCheck>> ContributeAsync(CancellationToken ct = default)
    {
        var configResult = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        if (configResult.Failed)
        {
            // The configuration itself is already reported on by the doctor.
            // Saying so again from here would be noise.
            return [];
        }

        var config = configResult.Value!;
        var editor = _editors.Describe(config);

        var checks = new List<DiagnosticCheck>
        {
            editor.IsInstalled
                ? DiagnosticCheck.Ok(Category, editor.Command, editor.Path!)
                : DiagnosticCheck.Ok(
                    Category,
                    editor.Command,
                    "not installed, or not on PATH. Projects will still launch; "
                    + "they just will not open in an editor."),
        };

        if (!editor.IsInstalled)
        {
            return checks;
        }

        checks.Add(editor.Profiles is null
            ? DiagnosticCheck.Ok(
                Category,
                "Profiles",
                "could not be read, so nothing here is checked against them")
            : DiagnosticCheck.Ok(
                Category,
                "Profiles",
                editor.Profiles.Count == 0
                    ? "none beyond the default"
                    : string.Join(", ", editor.Profiles)));

        // A mapping this editor has no way of applying is worth one line, not
        // one per entry: the setting is not wrong, it simply cannot be carried
        // out here, and somebody who has configured several is being told the
        // same thing several times.
        if (!editor.CanOpenAProfile && config.Editor.Profiles.Count > 0)
        {
            checks.Add(DiagnosticCheck.Warn(
                Category,
                "Profiles",
                $"configured per agent, but {editor.Command} "
                + (editor.Definition?.ProfileNote ?? "has no profile this can set.")));
        }

        // Only worth checking when the profiles could actually be read. Where
        // they could not, every name below would look missing.
        if (editor.Profiles is null)
        {
            return checks;
        }

        foreach (var (name, profile) in config.Editor.Profiles)
        {
            if (editor.IsMissing(profile))
            {
                checks.Add(DiagnosticCheck.Warn(
                    Category,
                    $"Profile for {name}",
                    $"'{profile}' is configured for {name} but no profile of that name exists "
                    + $"in {editor.Command}. Opening a project under it starts the editor "
                    + "without that configuration."));
            }
        }

        var projects = await _projects.ListAsync(ct).ConfigureAwait(false);

        if (projects.Failed)
        {
            return checks;
        }

        foreach (var project in projects.Value!)
        {
            var wanted = project.Entry.EditorProfile;

            if (wanted.Length > 0 && editor.IsMissing(wanted))
            {
                checks.Add(DiagnosticCheck.Warn(
                    Category,
                    $"Profile for {project.Entry.Slug}",
                    $"'{wanted}' is set on this project but no profile of that name exists "
                    + $"in {editor.Command}."));
            }
        }

        return checks;
    }
}

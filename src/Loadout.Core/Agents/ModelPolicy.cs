using Loadout.Models.Projects;

namespace Loadout.Core.Agents;

/// <summary>
/// Which model a launch should ask for, if any.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not an advisor. Choosing a model from how hard the work looks
/// would mean inferring difficulty from token counts, which is a guess wearing
/// a metric's clothes; this only carries out a choice somebody already made and
/// wrote down.
/// </para>
/// <para>
/// Saying nothing is a valid answer and the common one. A project that pins no
/// model leaves the agent on its own default, and the launcher passing a flag
/// nobody asked for would be its own kind of surprise.
/// </para>
/// </remarks>
public static class ModelPolicy
{
    /// <summary>The model for a launch, or null to leave the agent alone.</summary>
    /// <param name="manifest">The project, or null when there is none.</param>
    /// <param name="mode">The mode this session is in, if any.</param>
    public static string? For(ProjectManifest? manifest, string? mode)
    {
        if (manifest is null)
        {
            return null;
        }

        // The mode's own choice first: a project that pins a cheaper model for
        // review means it for review specifically, and having the project
        // default win would make the entry decorative.
        if (mode is { Length: > 0 }
            && manifest.Agents.ModelByMode.TryGetValue(mode, out var byMode)
            && !string.IsNullOrWhiteSpace(byMode))
        {
            return byMode.Trim();
        }

        return string.IsNullOrWhiteSpace(manifest.Agents.Model)
            ? null
            : manifest.Agents.Model.Trim();
    }
}

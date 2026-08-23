namespace Loadout.Models.Projects;

/// <summary>
/// A named slice of a project's context (spec section 34).
/// <para>
/// Profiles exist so that working on the database does not drag in the whole
/// frontend architecture. Loading everything every time is not merely wasteful:
/// it buries the material that matters for the task in material that does not.
/// </para>
/// </summary>
public sealed class ContextProfile
{
    /// <summary>Shown in the interactive picker, e.g. "Production investigation".</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Project-relative context files for this profile, e.g.
    /// <c>context/schema.md</c>. Added after the project's base context.
    /// </summary>
    public List<string> Context { get; set; } = [];

    /// <summary>
    /// Whether the shared global instructions are included. Almost always true;
    /// a profile that deliberately narrows to a single topic can turn it off.
    /// </summary>
    public bool IncludeGlobal { get; set; } = true;

    /// <summary>
    /// Restricts the profile to particular agents. Empty means every agent.
    /// </summary>
    public List<string> Agents { get; set; } = [];
}

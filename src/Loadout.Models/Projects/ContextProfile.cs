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

    /// <summary>
    /// Which specialists this profile expects to be relevant, and which it
    /// rules out. Overrides the project's own when set.
    /// </summary>
    public SpecialistPreferences Specialists { get; set; } = new();
}

/// <summary>
/// What a project or profile expects of the specialist resolver.
/// </summary>
/// <remarks>
/// <para>
/// Preferences are not instructions. A preferred specialist is one the project
/// expects to be relevant when the work points that way — not one to be put in
/// front of everybody on every launch. A project that lists PostgreSQL, Docker,
/// Kubernetes and Azure genuinely uses all four, and loading all four for
/// somebody fixing a null reference is exactly the "one enormous prompt" this
/// system exists to avoid.
/// </para>
/// <para>
/// Exclusion is different, and is honoured. Saying a specialist should never
/// load is a decision, not a hint, and the only thing that overrides it is
/// naming that specialist explicitly on the command line.
/// </para>
/// </remarks>
public sealed class SpecialistPreferences
{
    /// <summary>Specialist ids this project expects to be relevant, e.g. <c>database.postgresql</c>.</summary>
    public List<string> Preferred { get; set; } = [];

    /// <summary>Specialist ids that must not load for this project.</summary>
    public List<string> Excluded { get; set; } = [];

    /// <summary>
    /// Posture to start from when none is given on the command line, e.g.
    /// <c>review</c>. Empty means the launcher's default.
    /// </summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Whether anything has been said at all.</summary>
    public bool IsEmpty =>
        Preferred.Count == 0 && Excluded.Count == 0 && string.IsNullOrWhiteSpace(Mode);
}

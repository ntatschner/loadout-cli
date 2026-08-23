namespace Loadout.Models.Projects;

/// <summary>
/// A named environment a project can be worked on in (spec section 57).
/// <para>
/// The point is that production should be able to be much more restrictive than
/// development without anyone having to remember to make it so. Selecting an
/// environment changes which credentials resolve and which security profile
/// applies, in one step.
/// </para>
/// </summary>
public sealed class EnvironmentDefinition
{
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Security profile to apply, by name. Resolved from the workspace's
    /// profiles, falling back to the built-in set of spec section 58.
    /// </summary>
    public string? SecurityProfile { get; set; }

    /// <summary>
    /// Environment variables added to, or overriding, the project's base
    /// bindings. This is how production points at production credentials
    /// without development ever seeing them.
    /// </summary>
    public Dictionary<string, EnvironmentBinding> Environment { get; set; } = [];
}

namespace Loadout.Models.Projects;

/// <summary>
/// <c>registry/projects.yaml</c> — the workspace-wide index of projects
/// (spec section 11). Holds just enough to list and resolve projects without
/// reading every per-project manifest.
/// </summary>
public sealed class ProjectRegistry
{
    public int SchemaVersion { get; set; } = 1;

    public List<ProjectRegistryEntry> Projects { get; set; } = [];
}

/// <summary>One row of the project index.</summary>
public sealed class ProjectRegistryEntry
{
    public string Id { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Remote { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];

    public string DefaultAgent { get; set; } = "claude";
}

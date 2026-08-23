namespace Loadout.Models.Projects;

/// <summary>
/// A project as it exists on this machine: the shared registry entry joined to
/// the local path mapping. <see cref="LocalPath"/> is null when the project is
/// registered centrally but has not been cloned here yet — the case spec
/// section 28 requires the launcher to offer to fix rather than to error on.
/// </summary>
/// <param name="Entry">The shared registry row.</param>
/// <param name="LocalPath">Absolute path to the clone on this machine, or null if absent.</param>
/// <param name="LastLaunchedUtc">When this project was last launched here, for recent-project ordering (spec section 23).</param>
/// <param name="LaunchCount">How often it has been launched here, the secondary sort key.</param>
/// <param name="Pinned">Pinned projects sort first (spec section 23).</param>
public sealed record ProjectResolution(
    ProjectRegistryEntry Entry,
    string? LocalPath,
    DateTimeOffset? LastLaunchedUtc,
    int LaunchCount,
    bool Pinned)
{
    /// <summary>True when the repository is present locally and can be launched without cloning.</summary>
    public bool IsAvailableLocally => LocalPath is not null;
}

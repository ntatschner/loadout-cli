namespace Loadout.Models.Capabilities;

/// <summary>
/// What a person has said about a project's capabilities, kept beside its
/// profile in the workspace and never in the repository.
/// </summary>
/// <remarks>
/// Apart from the profile because the profile is rebuilt and thrown away, and
/// these are somebody's decisions: rebuilding must not lose them.
/// </remarks>
public sealed class CapabilityAnswers
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The checks this project will do without, each with who said so and when.</summary>
    public List<CapabilityNotNeeded> NotNeeded { get; set; } = [];

    /// <summary>The environment names last given, filled in the next time they are asked for.</summary>
    public List<string> Environments { get; set; } = [];

    /// <summary>The stack last chosen for release. Kept when no longer detected, so it comes back with its manifest.</summary>
    public string ReleaseStack { get; set; } = string.Empty;
}

/// <summary>One 'Not needed' answer.</summary>
public sealed class CapabilityNotNeeded
{
    /// <summary>The check, by its catalogue id.</summary>
    public string Check { get; set; } = string.Empty;

    public string By { get; set; } = string.Empty;

    public DateTimeOffset Utc { get; set; }
}

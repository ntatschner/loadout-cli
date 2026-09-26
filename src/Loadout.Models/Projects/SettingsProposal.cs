namespace Loadout.Models.Projects;

/// <summary>
/// A change to a project's <c>project.yaml</c> that somebody has suggested and
/// nobody has applied yet, kept at <c>projects/&lt;slug&gt;/proposals/settings.yaml</c>.
/// </summary>
/// <remarks>
/// Proposed rather than written because the settings decide what every later
/// session is told. An agent that could rewrite them for itself would be
/// choosing its own instructions with nobody watching; a proposal puts a person
/// between the suggestion and the file.
/// </remarks>
public sealed class SettingsProposal
{
    /// <summary>Who suggested it: <c>agent</c>, or a person's name.</summary>
    public string ProposedBy { get; set; } = string.Empty;

    public DateTimeOffset ProposedUtc { get; set; }

    /// <summary>Why each change is worth making, in the proposer's words.</summary>
    public string Reasons { get; set; } = string.Empty;

    /// <summary>
    /// A fingerprint of the manifest the proposal was made against, so one made
    /// before somebody else edited the file is refused rather than applied over
    /// their edit.
    /// </summary>
    public string Base { get; set; } = string.Empty;

    /// <summary>The whole manifest as it would be after applying.</summary>
    public ProjectManifest Manifest { get; set; } = new();
}

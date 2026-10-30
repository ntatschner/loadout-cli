namespace Loadout.Models.Capabilities;

/// <summary>What one check found.</summary>
/// <remarks>
/// Six states and no more, because what a state may do is fixed by which it
/// is: only a <see cref="Gap"/> is a thing the profile has seen missing.
/// <see cref="Unknown"/> and <see cref="NoPermission"/> mean it could not look,
/// and the profile never claims a gap it has not seen.
/// </remarks>
public enum CapabilityState
{
    /// <summary>It is there, and does the work in this repository.</summary>
    Present,

    /// <summary>It is there, and calls a shared workflow that does the work elsewhere.</summary>
    Delegated,

    /// <summary>Releases are published, but no workflow publishes them.</summary>
    ManualRelease,

    /// <summary>Looked for, and not there.</summary>
    Gap,

    /// <summary>Could not be looked for: no token, not asked, offline, or too slow.</summary>
    Unknown,

    /// <summary>GitHub was asked and refused; the result says which permission it wanted.</summary>
    NoPermission,
}

/// <summary>What one check found, and the one line of evidence for it.</summary>
public sealed class CapabilityResult
{
    /// <summary>The check, by its catalogue id, such as <c>ci</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public CapabilityState State { get; set; } = CapabilityState.Unknown;

    /// <summary>What was found, or why nothing could be: a file, a count, a refusal.</summary>
    public string Evidence { get; set; } = string.Empty;

    /// <summary>For <see cref="CapabilityState.NoPermission"/>, the permission GitHub wanted. Empty otherwise.</summary>
    public string Scope { get; set; } = string.Empty;
}

/// <summary>
/// What a project already has, such as CI, release and environments, as of
/// when it was built. Kept at <c>projects/&lt;slug&gt;/capabilities.yaml</c> in
/// the workspace, never in the repository.
/// </summary>
/// <remarks>
/// A cache for showing, not for deciding: anything that will act on the
/// profile builds it again first.
/// </remarks>
public sealed class CapabilityProfile
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The project, by slug.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>When it was built.</summary>
    public DateTimeOffset BuiltUtc { get; set; }

    /// <summary>
    /// How GitHub was read: the credential's kind, such as <c>gh token</c>, or
    /// why it was not, such as <c>not asked</c> or <c>no token</c>.
    /// </summary>
    public string GitHub { get; set; } = string.Empty;

    /// <summary>The kinds of project found in the repository, the one nearest its root first.</summary>
    public List<CapabilityStack> Stacks { get; set; } = [];

    /// <summary>One result for every check in the catalogue, in its order.</summary>
    public List<CapabilityResult> Checks { get; set; } = [];

    /// <summary>
    /// Things worth knowing that are not gaps, such as a stack no workflow
    /// appears to build. A guess from a workflow's steps, so never a gap.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>A kind of project found in the repository, and the manifest that says so.</summary>
public sealed class CapabilityStack
{
    /// <summary>The stack, such as <c>dotnet</c> or <c>node</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The manifest nearest the root, relative to it, with forward slashes.</summary>
    public string Manifest { get; set; } = string.Empty;
}

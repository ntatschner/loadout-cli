namespace Loadout.Models.Packs;

/// <summary>
/// A named set of specialists fetched from a Git remote.
/// </summary>
/// <remarks>
/// <para>
/// Declared in the workspace, because which packs a team uses is a decision the
/// team shares. What it is <em>not</em> is permission to run them: the content
/// of a pack becomes instructions an agent follows, and a file somebody else can
/// edit deciding what your agent is told is the trust boundary this whole type
/// exists to draw.
/// </para>
/// <para>
/// So the commit is pinned here and approved separately, on each machine, by
/// somebody who has looked at it. That is the same split command policy already
/// uses — the shared half may only propose, and the local half decides — and it
/// is worth having twice because the failure it prevents is the same one: a
/// change that reaches your machine because it reached somebody else's
/// repository.
/// </para>
/// </remarks>
public sealed class SpecialistPack
{
    /// <summary>What this pack is called locally.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Git remote it comes from.</summary>
    public string Remote { get; set; } = string.Empty;

    /// <summary>The branch or tag asked for.</summary>
    public string Ref { get; set; } = "main";

    /// <summary>
    /// The exact commit this pack is pinned to.
    /// </summary>
    /// <remarks>
    /// The lock. A branch moves and a tag can be moved; a commit cannot, so
    /// this is the only field that says what will actually be loaded. Updating
    /// it is a deliberate act that costs a fresh approval, because the content
    /// somebody approved is the content at a commit and nothing else.
    /// </remarks>
    public string Commit { get; set; } = string.Empty;

    /// <summary>What the pack is for, in a line, for whoever reads the list.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>Every pack a workspace declares.</summary>
public sealed class SpecialistPackFile
{
    public int SchemaVersion { get; set; } = 1;

    public List<SpecialistPack> Packs { get; set; } = [];
}

/// <summary>
/// A pack commit somebody on this machine has read and accepted.
/// </summary>
/// <remarks>
/// Machine-local and never shared, for the same reason a pre-approved command
/// is: approving is the act of taking responsibility for what an agent will be
/// told, and nobody can do that on somebody else's behalf by committing a file.
/// </remarks>
public sealed class PackApproval
{
    /// <summary>The pack, by name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The exact commit that was approved.</summary>
    public string Commit { get; set; } = string.Empty;

    /// <summary>Who approved it on this machine.</summary>
    public string ApprovedBy { get; set; } = string.Empty;

    public DateTimeOffset ApprovedUtc { get; set; }
}

/// <summary>Everything approved on this machine.</summary>
public sealed class PackApprovalFile
{
    public int SchemaVersion { get; set; } = 1;

    public List<PackApproval> Approvals { get; set; } = [];
}

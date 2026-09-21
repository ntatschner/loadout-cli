namespace Loadout.Models.Teams;

/// <summary>
/// Machinery a declaration can lean on, named so a team can ask for it.
/// </summary>
/// <remarks>
/// <para>
/// A declaration is prose in a brief and enforces nothing by itself. The first
/// real one — keep what you work out, and reuse it — only worked because seven
/// separate things were built to match it: a shelf in the team's directory, a
/// record format, a role that could act on it, a gate, the brief text that
/// explains the format, commands to read it, and a check that the team could
/// do any of it.
/// </para>
/// <para>
/// This is that bundle, as a file, so the next one does not need seven commits
/// of C#. It layers built-in, pack, workspace, project like everything else
/// here, so a pack can ship one and a project can replace it.
/// </para>
/// <para>
/// It cannot define a gate, and that is deliberate rather than unfinished. A
/// capability names a gate that already exists; what an agent may actually run
/// is decided by this machine's own configuration, in C#, behind the same
/// boundary as everything else. A file that could describe its own gate would
/// be a shared file deciding what agents may execute — which is exactly what
/// moving trust out of the team's directory existed to prevent, after a node
/// wrote itself a record claiming to be trusted and ran unasked.
/// </para>
/// </remarks>
public sealed class DeclarationCapability
{
    /// <summary>What a team asks for by name, lowercase and hyphenated.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>A line for a listing.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// The subdirectory of the team's directory this keeps things in.
    /// </summary>
    /// <remarks>
    /// Empty for a capability that keeps nothing, which is allowed: not every
    /// standing practice has a shelf.
    /// </remarks>
    public string Shelf { get; set; } = string.Empty;

    /// <summary>
    /// Roles that can act on what is kept. A team needs at least one of them.
    /// </summary>
    /// <remarks>
    /// Any of them rather than all, because the point is whether the team can
    /// do the thing at all. A team asking for a capability and having none of
    /// its roles is told so: the declaration would go into every brief of
    /// every run and nothing could act on it.
    /// </remarks>
    public List<string> Roles { get; set; } = [];

    /// <summary>
    /// The gate that governs acting on what is kept, by name, or empty.
    /// </summary>
    /// <remarks>
    /// Named, never defined. A capability that named a gate nothing implements
    /// is a capability that promises a decision nobody makes, and is refused
    /// when the catalogue is read.
    /// </remarks>
    public string Gate { get; set; } = string.Empty;

    /// <summary>
    /// What a node is told about this, verbatim, in every brief of the team.
    /// </summary>
    /// <remarks>
    /// Verbatim because nothing here summarises an instruction. This is the
    /// text that tells a node the record format and where it goes, and a
    /// paraphrase of it is a different format.
    /// </remarks>
    public string Brief { get; set; } = string.Empty;
}

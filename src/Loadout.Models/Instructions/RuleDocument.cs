namespace Loadout.Models.Instructions;

/// <summary>
/// One path-scoped instruction file.
/// <para>
/// The point of scoping is that an instruction which only matters for database
/// work should not be loaded when nobody is touching the database. An
/// always-loaded instruction file is paid for in every single session, so the
/// difference between "scoped" and "always" is the difference between a rule
/// that costs nothing when irrelevant and one that costs on every turn.
/// </para>
/// </summary>
/// <param name="Name">File name without extension, used to address it.</param>
/// <param name="Path">Absolute path to the file.</param>
/// <param name="Description">The frontmatter description.</param>
/// <param name="Globs">Path patterns this rule applies to. Empty when unscoped.</param>
/// <param name="AlwaysApply">True when the rule loads regardless of what is being worked on.</param>
/// <param name="Body">Everything after the frontmatter.</param>
/// <param name="Bytes">Size on disk, which is what an always-loaded rule costs.</param>
public sealed record RuleDocument(
    string Name,
    string Path,
    string Description,
    IReadOnlyList<string> Globs,
    bool AlwaysApply,
    string Body,
    long Bytes)
{
    /// <summary>
    /// A rule with neither globs nor an explicit always-apply flag.
    /// <para>
    /// Reported rather than assumed either way. Treating it as always-apply
    /// would silently add it to every session; treating it as scoped would
    /// silently drop it from all of them. Both are worse than saying so.
    /// </para>
    /// </summary>
    public bool IsUnscoped => !AlwaysApply && Globs.Count == 0;
}

/// <summary>How much instruction text is loaded whatever the task.</summary>
/// <param name="CoreBytes">Size of the project's always-loaded core instructions.</param>
/// <param name="AlwaysApplyRules">Rules that load every session, with their sizes.</param>
/// <param name="ScopedRules">Rules that load only on a path match.</param>
/// <param name="UnscopedRules">Rules whose scope could not be determined.</param>
public sealed record InstructionBudget(
    long CoreBytes,
    IReadOnlyList<RuleDocument> AlwaysApplyRules,
    IReadOnlyList<RuleDocument> ScopedRules,
    IReadOnlyList<RuleDocument> UnscopedRules)
{
    /// <summary>Bytes paid for in every session regardless of the task.</summary>
    public long AlwaysLoadedBytes => CoreBytes + AlwaysApplyRules.Sum(r => r.Bytes);

    /// <summary>Bytes that load only when the work touches a matching path.</summary>
    public long ScopedBytes => ScopedRules.Sum(r => r.Bytes);
}

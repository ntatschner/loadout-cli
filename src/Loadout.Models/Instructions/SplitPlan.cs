namespace Loadout.Models.Instructions;

/// <summary>
/// One rule file a split would produce.
/// </summary>
/// <param name="Name">File name without extension.</param>
/// <param name="Description">Frontmatter description, so a listing can say when to read it.</param>
/// <param name="Globs">Paths the rule applies to.</param>
/// <param name="Body">Content moved out of the source, verbatim.</param>
/// <param name="Sections">Headings whose content ended up here, for the report.</param>
public sealed record SplitRule(
    string Name,
    string Description,
    IReadOnlyList<string> Globs,
    string Body,
    IReadOnlyList<string> Sections);

/// <summary>
/// What a split would do to an instruction file.
/// <para>
/// Always produced and shown before anything is written. Splitting rewrites a
/// document somebody may have spent a year on, and a preview plus a proof that
/// nothing was lost is the difference between a tool people will run on it and
/// one they will not.
/// </para>
/// </summary>
/// <param name="SourcePath">The instruction file being split.</param>
/// <param name="Core">What would remain in the source file.</param>
/// <param name="Rules">The scoped rules that would be created.</param>
/// <param name="MissingLines">
/// Lines present in the source and absent from the outputs.
/// <para>
/// Must be empty for a split to be applied. It is the whole safety argument:
/// every non-blank line is accounted for by count, so a routing mistake shows
/// up as a refusal rather than as content that quietly disappeared.
/// </para>
/// </param>
/// <param name="Applied">False for a preview.</param>
/// <param name="BackupId">Snapshot taken before writing, when applied.</param>
public sealed record SplitPlan(
    string SourcePath,
    string Core,
    IReadOnlyList<SplitRule> Rules,
    IReadOnlyList<string> MissingLines,
    bool Applied = false,
    string? BackupId = null)
{
    /// <summary>Whether every line in the source is accounted for in the outputs.</summary>
    public bool IsLossless => MissingLines.Count == 0;

    /// <summary>Bytes that would stay in the always-loaded core file.</summary>
    public long CoreBytes => System.Text.Encoding.UTF8.GetByteCount(Core);

    /// <summary>Bytes that would become loadable on demand instead of always.</summary>
    public long MovedBytes => Rules.Sum(r => System.Text.Encoding.UTF8.GetByteCount(r.Body));
}

/// <summary>How one section of the source should be routed.</summary>
public sealed class SectionRoute
{
    /// <summary>
    /// Heading to match, with <c>*</c> as a wildcard so a map survives small
    /// edits to a heading's wording.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Rule the section moves into.</summary>
    public string Rule { get; set; } = string.Empty;
}

/// <summary>Routes an individual bullet out of a section that otherwise stays.</summary>
public sealed class BulletRoute
{
    /// <summary>Heading the bullet lives under.</summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>Text the bullet must contain, matched case-insensitively.</summary>
    public string Contains { get; set; } = string.Empty;

    /// <summary>Rule the bullet moves into.</summary>
    public string Rule { get; set; } = string.Empty;
}

/// <summary>A rule the map declares, with the scope it should carry.</summary>
public sealed class RuleTarget
{
    /// <summary>File name the rule is written under, without extension.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One line saying when to read it.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Paths the rule applies to. A rule with none is refused: it would load always.</summary>
    public List<string> Globs { get; set; } = [];
}

/// <summary>
/// The plan for decomposing an instruction file.
/// <para>
/// Written by hand and kept in the workspace, because deciding which
/// instructions matter for which paths is a judgement about the project. The
/// splitter moves text; it does not decide what the text means.
/// </para>
/// </summary>
public sealed class SplitMap
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Rules to create, each with the scope it will carry.</summary>
    public List<RuleTarget> Rules { get; set; } = [];

    /// <summary>Sections to move wholesale.</summary>
    public List<SectionRoute> Sections { get; set; } = [];

    /// <summary>Bullets to move out of sections that otherwise stay in the core.</summary>
    public List<BulletRoute> Bullets { get; set; } = [];
}

namespace Loadout.Models.Instructions;

/// <summary>
/// What a project's documentation is expected to hold, and what its claims can
/// be checked against.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the workspace beside the project's other configuration rather than
/// in the repository, which is the same promise <c>loadout protect</c> exists to
/// keep: the repository holds source, and the rules about it live elsewhere.
/// </para>
/// <para>
/// Everything here is optional, and a project without a policy still gets the
/// checks that need no configuration — a link either resolves or it does not.
/// This is for the ones that cannot be answered without being told what to
/// count.
/// </para>
/// </remarks>
public sealed class DocsPolicy
{
    /// <summary>Directory holding the documentation, relative to the repository.</summary>
    public string Root { get; set; } = "docs";

    /// <summary>
    /// Claims in prose that can be counted, as a noun and the files it means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The class of drift that rots invisibly, because the sentence still reads
    /// perfectly: "there are 73 specialists" was left saying 71 while the
    /// library grew, and nothing about the page looked wrong. A number is the
    /// one kind of prose with a right answer, and this is the mapping that says
    /// where the right answer lives.
    /// </para>
    /// <para>
    /// Keyed by the singular noun. The plural is derived, because writing both
    /// out is the sort of configuration nobody keeps in step.
    /// </para>
    /// </remarks>
    public Dictionary<string, string> Counts { get; set; } = [];

    /// <summary>
    /// Pages whose numbers are about something other than this repository.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counting assumes a noun means the same thing on every page, and that is
    /// not always true. This project's own <c>specialists-architecture.md</c> is
    /// a survey written before implementation, of a proposed external bundle:
    /// its "52 specialists" and "77 markdown files" are about somebody else's
    /// library. Counted against this repository every number in it is wrong, and
    /// none of them is stale.
    /// </para>
    /// <para>
    /// Matched as a suffix of the path, so <c>specialists-architecture.md</c>
    /// covers <c>docs/specialists-architecture.md</c> without the policy having
    /// to repeat the root it already declared.
    /// </para>
    /// </remarks>
    public List<string> CountsExclude { get; set; } = [];

    /// <summary>Whether anything here can be acted on.</summary>
    public bool IsUsable => Counts.Count > 0;
}

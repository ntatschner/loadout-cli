namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// Something the page asked to do to an idea or a dump, in the words it sent.
/// </summary>
/// <remarks>
/// <para>
/// One record for every verb, because they share nearly every field and the
/// page is simpler for sending one shape. Which fields a verb reads is the
/// mapping's business, and each is tested field by field: a field the page
/// sends and nothing reads arrives as null, the command runs with its default,
/// and the page says it worked. That has happened four times on this server.
/// </para>
/// <para>
/// Nothing here is acted on by the server. It is handed to whatever runs the
/// command somebody would have typed.
/// </para>
/// </remarks>
/// <param name="Verb">add, refine, answer, choose, keep, drop, improve, accept, remove, dump or apply.</param>
/// <param name="Id">The idea, or for apply the dump.</param>
/// <param name="Project">The list it is on: a project's slug, or null for the workspace-wide one.</param>
/// <param name="Text">A new idea, or the notes to dump.</param>
/// <param name="Question">Which question, for answer.</param>
/// <param name="Answer">The answer.</param>
/// <param name="Layer">Which layer, for choose.</param>
/// <param name="Option">Which option.</param>
/// <param name="Pieces">Which pieces, for keep and drop.</param>
/// <param name="Piece">Which piece, or <c>plan</c>, for improve.</param>
/// <param name="Request">What to change.</param>
/// <param name="To">Where an accepted idea goes, or where applied pieces go.</param>
/// <param name="NewProject">A name for a project to make for an accepted idea.</param>
/// <param name="Only">Which pieces of a dump to record, or none for all of them.</param>
public sealed record IdeaAction(
    string Verb,
    string? Id = null,
    string? Project = null,
    string? Text = null,
    string? Question = null,
    string? Answer = null,
    string? Layer = null,
    string? Option = null,
    IReadOnlyList<string>? Pieces = null,
    string? Piece = null,
    string? Request = null,
    string? To = null,
    string? NewProject = null,
    IReadOnlyList<int>? Only = null);

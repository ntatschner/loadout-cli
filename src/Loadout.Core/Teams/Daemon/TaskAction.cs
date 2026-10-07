namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// Something the page asked to do to a task, in the words it sent.
/// </summary>
/// <remarks>
/// <para>
/// One record for every verb, as <see cref="IdeaAction"/> is, and tested
/// field by field for the same reason: a field the page sends and nothing
/// reads arrives as null, the command runs with its default, and the page says
/// it worked.
/// </para>
/// <para>
/// Nothing here is acted on by the server. It is handed to whatever runs the
/// command somebody would have typed.
/// </para>
/// </remarks>
/// <param name="Verb">add, state, edit, remove or run.</param>
/// <param name="Id">The task. For add, an id to use, or null for one made from the title.</param>
/// <param name="Project">The list it is on: a project's slug, or null for the workspace-wide one.</param>
/// <param name="Title">What the task is, for add and edit.</param>
/// <param name="Note">A note, for add and edit; for state, why it is blocked.</param>
/// <param name="State">The state to declare: open, doing, blocked, done or dropped. For edit, the one it has now.</param>
public sealed record TaskAction(
    string Verb,
    string? Id = null,
    string? Project = null,
    string? Title = null,
    string? Note = null,
    string? State = null);

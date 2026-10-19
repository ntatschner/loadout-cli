using Loadout.Core.Tasks;
using Loadout.Models.Configuration;
using Loadout.Models.Results;

namespace Loadout.Core.Lists;

/// <summary>
/// The lists whose query is saved, one per view, and what each starts with
/// when nothing has been saved for it.
/// </summary>
/// <remarks>
/// A view is saved in Studio's settings and read by every surface, so a
/// filter set in Studio is the one the TUI opens with and 'task list --saved'
/// applies. A list joins here when it takes the shared query.
/// </remarks>
public static class ListViews
{
    /// <summary>One list whose query is kept.</summary>
    /// <param name="Name">The view's name, as stored and as its config key ends.</param>
    /// <param name="DefaultQuery">What it shows when nothing is saved.</param>
    /// <param name="Check">Whether a query names only fields this list has.</param>
    public sealed record View(string Name, string DefaultQuery, Func<ListQuery, OperationResult> Check)
    {
        /// <summary>The config key its saved query is set with.</summary>
        public string Key => $"studio-query-{Name}";
    }

    public static readonly IReadOnlyList<View> All =
    [
        new("tasks", TaskFields.DefaultQuery, TaskFields.All.Check),
        new("projects", string.Empty, ProjectFields.All.Check),
        new("ideas", string.Empty, IdeaFields.All.Check),
    ];

    /// <summary>The view with this name, or null.</summary>
    public static View? Find(string name) =>
        All.FirstOrDefault(view => string.Equals(view.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The query saved for a view, or null when nothing is.</summary>
    public static string? Saved(StudioSettings? studio, string name) =>
        studio?.Views is { } views && views.TryGetValue(name, out var saved) && saved.Length > 0 ? saved : null;

    /// <summary>
    /// A query checked for a view and written out as it will be stored, or
    /// why it cannot be.
    /// </summary>
    public static OperationResult<string> Normalise(View view, string text)
    {
        ArgumentNullException.ThrowIfNull(view);

        var parsed = ListQuery.Parse(text);

        if (parsed.Failed)
        {
            return OperationResult<string>.Fail(parsed.Error!, parsed.ExitCode);
        }

        var checkedQuery = view.Check(parsed.Value!);

        return checkedQuery.Failed
            ? OperationResult<string>.Fail(checkedQuery.Error!, checkedQuery.ExitCode)
            : OperationResult<string>.Ok(parsed.Value!.ToString());
    }
}

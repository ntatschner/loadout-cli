namespace Loadout.Models.Configuration;

/// <summary>The words Studio's Map and Lens filters are kept in, shared by the config keys and the page.</summary>
public static class StudioMap
{
    /// <summary>Every kind of line, in the order the filter bar shows them.</summary>
    public static IReadOnlyList<string> Kinds { get; } = ["runs", "sessions", "ideas", "tasks"];

    /// <summary>Every state a line can be in, in the order the filter bar shows them.</summary>
    public static IReadOnlyList<string> States { get; } = ["waiting", "running", "failed", "ended"];

    /// <summary>
    /// How the project filter names work outside any project. Brackets, so no
    /// project's slug can ever be the same.
    /// </summary>
    public const string NoProject = "(no project)";
}

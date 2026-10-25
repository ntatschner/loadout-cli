using System.Text.Json;

namespace Loadout.Core.Teams;

/// <summary>One line of a run's journal, as it goes into the file.</summary>
/// <remarks>
/// The runner writes most of a journal, <see cref="RunCloser"/> writes the
/// end of one whose runner died, and <see cref="BlockedRunReader"/> writes
/// what a person decided about one that ended short; they live in different
/// assemblies. One serialiser for all of them, so they cannot drift into
/// lines the fold reads differently.
/// </remarks>
public static class RunJournalLine
{
    private static readonly JsonSerializerOptions Line = new() { WriteIndented = false };

    /// <summary>The line, without its newline.</summary>
    /// <param name="at">When it happened.</param>
    /// <param name="run">The run's identifier.</param>
    /// <param name="node">Which node, or null for the run itself.</param>
    /// <param name="kind">What happened.</param>
    /// <param name="data">Whatever that kind carries.</param>
    public static string Serialize(DateTimeOffset at, string run, string? node, string kind, object? data) =>
        JsonSerializer.Serialize(new { at, run, node, kind, data }, Line);
}

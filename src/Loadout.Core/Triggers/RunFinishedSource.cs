using System.Globalization;
using System.Text.Json;
using Loadout.Core.Teams;

namespace Loadout.Core.Triggers;

/// <summary>
/// Turns team runs reaching their end into events, by reading the runs this
/// machine already keeps.
/// </summary>
/// <remarks>
/// <para>
/// It costs nothing to watch: the daemon reads the same run journals for the
/// page every few seconds. Read once a pass, from the newest fifty runs.
/// </para>
/// <para>
/// Where it had got to is kept in a cursor file,
/// <c>&lt;State&gt;/triggers/cursors/run-finished.json</c>: the latest time a
/// run was seen to finish. A time and not a run id, for the reason the
/// run-finished schedule gives: ids sort by when a run began, so a long run
/// that began first and finished last would sort below one already seen. The
/// first look writes the cursor down and posts nothing, so turning triggers on
/// does not answer every run that ever finished.
/// </para>
/// </remarks>
public sealed class RunFinishedSource
{
    /// <summary>What its events name as their source.</summary>
    public const string Name = "daemon";

    private readonly IRunJournal _runs;
    private readonly string _cursor;

    /// <param name="state">The state directory.</param>
    /// <param name="runs">The run journals.</param>
    public RunFinishedSource(string state, IRunJournal runs)
    {
        _runs = runs;
        _cursor = Path.Combine(state, "triggers", "cursors", "run-finished.json");
    }

    /// <summary>
    /// The runs that have finished since the last look, oldest first, as events;
    /// the cursor is moved past them.
    /// </summary>
    /// <remarks>
    /// The cursor is written before the events are handed on. A daemon killed
    /// between the two loses those events rather than posting them twice; the
    /// journal of the spine is the record of what was acted on, and an event
    /// acted on twice spends twice.
    /// </remarks>
    public IReadOnlyList<TriggerEvent> Look()
    {
        var finished = _runs.List(50)
            .Select(_runs.Summarise)
            .Where(one => one.Succeeded && one.Value!.Finished is not null)
            .Select(one => one.Value!)
            .OrderBy(one => one.Finished)
            .ToList();

        var newest = finished.Count > 0 ? finished[^1].Finished!.Value : (DateTimeOffset?)null;

        if (ReadCursor() is not { } seen)
        {
            Save(newest ?? DateTimeOffset.MinValue);

            return [];
        }

        var fresh = finished.Where(one => one.Finished > seen).ToList();

        if (fresh.Count == 0)
        {
            return [];
        }

        Save(newest!.Value);

        return [.. fresh.Select(Event)];
    }

    /// <summary>The event for one finished run.</summary>
    public static TriggerEvent Event(RunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["team"] = run.Team,
            ["run"] = run.RunId,
            ["outcome"] = run.Outcome ?? run.Ended ?? "unknown",
        };

        if (run.Project is { Length: > 0 } project)
        {
            fields["project"] = project;
        }

        return new TriggerEvent(
            $"run-finished:{run.RunId}",
            run.Finished ?? run.Started,
            Name,
            TriggerEventKinds.RunFinished,
            $"run:{run.RunId}",
            run.Project,
            fields,
            RunOrigin.Parse(run.Origin)?.Origins ?? []);
    }

    private DateTimeOffset? ReadCursor()
    {
        try
        {
            if (!File.Exists(_cursor))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_cursor));

            return document.RootElement.TryGetProperty("seen", out var seen)
                && DateTimeOffset.TryParse(seen.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                    ? at
                    : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A cursor that cannot be read is a first look: better to miss a
            // run that finished in the gap than to answer every run again.
            return null;
        }
    }

    private void Save(DateTimeOffset seen)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cursor)!);

        var aside = _cursor + ".tmp";
        File.WriteAllText(aside, JsonSerializer.Serialize(new { seen = seen.ToString("o", CultureInfo.InvariantCulture) }));
        File.Move(aside, _cursor, overwrite: true);
    }
}

using System.Globalization;
using System.Text.Json;
using Loadout.Core.Delivery;
using Loadout.Models.Delivery;

namespace Loadout.Core.Triggers;

/// <summary>
/// Turns work landing and being released into events: a waiting branch
/// marked merged, and a release cut, on whichever host or with local git.
/// </summary>
/// <remarks>
/// <para>
/// Read from what this machine already keeps: each project's landings, and
/// the release log <see cref="ReleaseLog"/> appends to. Unlike pr-merged,
/// which comes from GitHub, these cover GitLab, Azure DevOps and local git
/// alike, and only the branches Loadout was waiting on.
/// </para>
/// <para>
/// Where it had got to is kept in a cursor file,
/// <c>&lt;State&gt;/triggers/cursors/landing.json</c>, as the latest time
/// seen, for the reason <see cref="RunFinishedSource"/> keeps one. The first
/// look writes it down and posts nothing.
/// </para>
/// </remarks>
public sealed class LandingSource
{
    /// <summary>What its events name as their source.</summary>
    public const string Name = "delivery";

    private readonly ILandingService _landings;
    private readonly string _state;
    private readonly string _cursor;

    /// <param name="state">The state directory.</param>
    /// <param name="landings">Each project's landings.</param>
    public LandingSource(string state, ILandingService landings)
    {
        _state = state;
        _landings = landings;
        _cursor = Path.Combine(state, "triggers", "cursors", "landing.json");
    }

    /// <summary>What has landed or been released since the last look, oldest first, as events.</summary>
    public async Task<IReadOnlyList<TriggerEvent>> LookAsync(CancellationToken ct = default)
    {
        var events = new List<(DateTimeOffset At, TriggerEvent Event)>();

        foreach (var project in _landings.Projects())
        {
            var loaded = await _landings.LoadAsync(project, ct).ConfigureAwait(false);

            if (loaded.Failed)
            {
                continue;
            }

            events.AddRange(loaded.Value!.Landings
                .Where(one => one.State == LandingState.Merged)
                .Select(one => (one.UpdatedUtc, Landed(project, one))));
        }

        events.AddRange(ReleaseLog.Read(_state).Select(one => (one.At, Released(one))));

        var newest = events.Count > 0 ? events.Max(one => one.At) : (DateTimeOffset?)null;

        if (ReadCursor() is not { } seen)
        {
            Save(newest ?? DateTimeOffset.MinValue);

            return [];
        }

        var fresh = events.Where(one => one.At > seen).OrderBy(one => one.At).Select(one => one.Event).ToList();

        if (fresh.Count > 0)
        {
            Save(newest!.Value);
        }

        return fresh;
    }

    /// <summary>The event for a branch marked merged.</summary>
    public static TriggerEvent Landed(string project, BranchLanding landing)
    {
        ArgumentNullException.ThrowIfNull(landing);

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["project"] = project,
            ["branch"] = landing.Branch,
            ["team"] = landing.Team,
            ["run"] = landing.RunId,
            ["task"] = landing.Task,
            ["url"] = landing.PullRequest,
        };

        return new TriggerEvent(
            $"branch-landed:{project}:{landing.Branch}",
            landing.UpdatedUtc,
            Name,
            TriggerEventKinds.BranchLanded,
            $"branch:{project}:{landing.Branch}",
            project,
            fields,
            []);
    }

    /// <summary>The event for a release cut.</summary>
    public static TriggerEvent Released(ReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);

        return new TriggerEvent(
            $"released:{release.Project}:{release.Tag}",
            release.At,
            Name,
            TriggerEventKinds.Released,
            $"release:{release.Project}:{release.Tag}",
            release.Project,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["project"] = release.Project,
                ["tag"] = release.Tag,
                ["version"] = release.Version,
                ["commit"] = release.Commit,
                ["recipe"] = release.Recipe,
            },
            []);
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

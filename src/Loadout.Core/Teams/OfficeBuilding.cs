namespace Loadout.Core.Teams;

/// <summary>One occupied floor of the building.</summary>
/// <param name="Number">The floor, counting up from 1 above the lobby.</param>
/// <param name="Run">The run on it.</param>
/// <param name="Part">Which of the run's floors: 0 its first, 1 and on where it has spilled upstairs.</param>
/// <param name="People">How many of the run's people are on this floor.</param>
/// <param name="State">How the run is doing, for the strip along the floor's edge: waiting, failed, working, quiet or done.</param>
/// <param name="Dark">Whether its lights are out: the run has finished and the floor is only being kept.</param>
/// <param name="Nodes">The run's nodes on this floor, lead first: who the page seats here.</param>
public sealed record BuildingFloor(int Number, string Run, int Part, int People, string State, bool Dark, IReadOnlyList<string> Nodes);

/// <summary>The building as it stands: how tall, and who is on which floor.</summary>
/// <param name="Floors">How many floors the tower is drawn with.</param>
/// <param name="Occupied">Every floor with a run on it, lowest first.</param>
public sealed record BuildingView(int Floors, IReadOnlyList<BuildingFloor> Occupied);

/// <summary>
/// Which run is on which floor of the building, remembered from one look to
/// the next.
/// </summary>
/// <remarks>
/// <para>
/// A run seen for the first time is given as many floors as it needs at once,
/// lowest free first. After that it moves only after the rules' waits: a team
/// that has outgrown its floors spills onto another once it has been too big
/// for <see cref="OfficeMoves.SpillUpSeconds"/>, preferring the floor directly
/// above so it stays together; one that fits on fewer gives the top one back
/// once it has for <see cref="OfficeMoves.GiveBackSeconds"/>. The long wait
/// down and the short one up are so that a run briefing and finishing workers
/// does not bounce between floors.
/// </para>
/// <para>
/// A finished run keeps its floors, lights out, for
/// <see cref="OfficeMoves.KeptMinutes"/>, so it can still be visited, and then
/// they are freed for the next run. The tower is never shorter than the rules'
/// fewest floors.
/// </para>
/// <para>
/// Time is passed in rather than read, so the waits can be tested without
/// waiting.
/// </para>
/// </remarks>
public sealed class OfficeBuilding
{
    private readonly Dictionary<string, Tenancy> _tenants = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private sealed class Tenancy
    {
        public List<int> Floors { get; } = [];

        public DateTimeOffset? OverSince { get; set; }

        public DateTimeOffset? UnderSince { get; set; }
    }

    /// <summary>Take in the runs as they are now, and say who is on which floor.</summary>
    /// <param name="runs">Every run the office could show.</param>
    /// <param name="capacity">How many people a floor seats, lead included.</param>
    /// <param name="rules">For the fewest floors and the waits.</param>
    /// <param name="now">The time now.</param>
    public BuildingView Update(IReadOnlyList<RunSummary> runs, int capacity, OfficeRules rules, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(rules);

        var moves = rules.Moves ?? new OfficeMoves();
        var seat = Math.Max(1, capacity);

        lock (_gate)
        {
            // Who is still in the building: every run going, and every run
            // finished within the time a floor is kept for it.
            var present = runs
                .Where(run => run.Running || (run.Finished is { } ended && now - ended < TimeSpan.FromMinutes(moves.KeptMinutes)))
                .OrderBy(run => run.Started)
                .ThenBy(run => run.RunId, StringComparer.Ordinal)
                .ToList();

            foreach (var gone in _tenants.Keys.Except(present.Select(run => run.RunId)).ToList())
            {
                _tenants.Remove(gone);
            }

            foreach (var run in present)
            {
                var need = Need(run, seat);

                if (!_tenants.TryGetValue(run.RunId, out var tenancy))
                {
                    tenancy = _tenants[run.RunId] = new Tenancy();

                    while (tenancy.Floors.Count < need)
                    {
                        tenancy.Floors.Add(Free(tenancy));
                    }

                    continue;
                }

                // A finished run's floors stay as they were until it is gone.
                if (!run.Running)
                {
                    tenancy.OverSince = null;
                    tenancy.UnderSince = null;

                    continue;
                }

                if (need > tenancy.Floors.Count)
                {
                    tenancy.UnderSince = null;
                    tenancy.OverSince ??= now;

                    if (now - tenancy.OverSince.Value >= TimeSpan.FromSeconds(moves.SpillUpSeconds))
                    {
                        while (tenancy.Floors.Count < need)
                        {
                            tenancy.Floors.Add(Free(tenancy));
                        }

                        tenancy.OverSince = null;
                    }
                }
                else if (need < tenancy.Floors.Count)
                {
                    tenancy.OverSince = null;
                    tenancy.UnderSince ??= now;

                    if (now - tenancy.UnderSince.Value >= TimeSpan.FromSeconds(moves.GiveBackSeconds))
                    {
                        tenancy.Floors.RemoveRange(need, tenancy.Floors.Count - need);
                        tenancy.UnderSince = null;
                    }
                }
                else
                {
                    tenancy.OverSince = null;
                    tenancy.UnderSince = null;
                }
            }

            var occupied = new List<BuildingFloor>();

            foreach (var run in present)
            {
                var floors = _tenants[run.RunId].Floors;
                var here = Present(run);
                var state = State(run);

                for (var part = 0; part < floors.Count; part++)
                {
                    // Filled from the first floor up: the lead and the first
                    // workers downstairs, whoever does not fit above.
                    var on = part == floors.Count - 1
                        ? here.Skip(seat * part).ToList()
                        : here.Skip(seat * part).Take(seat).ToList();

                    occupied.Add(new BuildingFloor(floors[part], run.RunId, part, on.Count, state, !run.Running, on));
                }
            }

            occupied.Sort((a, b) => a.Number.CompareTo(b.Number));

            var tallest = occupied.Count == 0 ? 0 : occupied[^1].Number;

            return new BuildingView(Math.Max(rules.MinFloors ?? 10, tallest), occupied);
        }
    }

    /// <summary>The lowest floor nobody has, the one above this tenant's top floor first.</summary>
    private int Free(Tenancy tenancy)
    {
        var taken = _tenants.Values.SelectMany(one => one.Floors).ToHashSet();

        if (tenancy.Floors.Count > 0 && !taken.Contains(tenancy.Floors[^1] + 1))
        {
            return tenancy.Floors[^1] + 1;
        }

        var floor = 1;

        while (taken.Contains(floor))
        {
            floor++;
        }

        return floor;
    }

    /// <summary>A run's people still in the building, the lead first as the floors seat them.</summary>
    private static List<string> Present(RunSummary run) =>
    [
        .. run.Nodes
            .Where(node => OfficeIntent.For(run, node).Place != OfficePlace.Gone)
            .OrderBy(node => node.Role == "role.project-lead" ? 0 : 1)
            .Select(node => node.Node),
    ];

    private static int Need(RunSummary run, int seat) => Math.Max(1, (Present(run).Count + seat - 1) / seat);

    /// <summary>The state the floor's strip shows: what most needs seeing first.</summary>
    private static string State(RunSummary run)
    {
        if (!run.Running)
        {
            return run.Nodes.Any(node => node.State is "failed") ? "failed" : "done";
        }

        var lamps = run.Nodes.Select(node => OfficeIntent.For(run, node).Lamp).ToList();

        return lamps.Contains(OfficeLamp.Waiting) ? "waiting"
            : lamps.Contains(OfficeLamp.Failed) ? "failed"
            : lamps.Contains(OfficeLamp.Working) ? "working"
            : "quiet";
    }
}

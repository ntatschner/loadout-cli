namespace Loadout.Core.Teams;

/// <summary>One occupied floor of the building.</summary>
/// <param name="Number">The floor, counting up from 1 above the lobby.</param>
/// <param name="Run">The run on it.</param>
/// <param name="Part">Which of the run's floors: 0 its first, 1 and on where it has spilled upstairs.</param>
/// <param name="People">How many of the run's people are on this floor.</param>
/// <param name="State">How the run is doing, for the strip along the floor's edge: waiting, failed, working, quiet or done.</param>
/// <param name="Dark">Whether its lights are out: the run has finished and the floor is only being kept.</param>
/// <param name="Nodes">The run's nodes on this floor, lead first: who the page seats here.</param>
/// <param name="Bay">Its first bay on the floor, counting from 0 at the west.</param>
/// <param name="Bays">How many bays it has there, side by side.</param>
public sealed record BuildingFloor(int Number, string Run, int Part, int People, string State, bool Dark, IReadOnlyList<string> Nodes, int Bay = 0, int Bays = 1);

/// <summary>A run's place on a floor teams share: which bays, and how many people sit there.</summary>
/// <param name="Run">The run.</param>
/// <param name="Part">Which of the run's floors: 0 its first.</param>
/// <param name="Bay">Its first bay, counting from 0 at the west.</param>
/// <param name="Bays">How many bays, side by side from the first.</param>
/// <param name="People">How many of its people sit on this floor, lead included.</param>
public sealed record OfficeTenant(string Run, int Part, int Bay, int Bays, int People);

/// <summary>
/// The building's own rooms a floor is offered: those the rules give the whole
/// tower rather than every floor, such as a library, that no floor below has
/// taken. The floor takes what fits in its band.
/// </summary>
/// <param name="Kinds">The kinds of room on offer.</param>
/// <param name="People">Everybody in the building, which they are counted from.</param>
public sealed record OfficeBuildingRooms(IReadOnlyCollection<string> Kinds, int People);

/// <summary>The building as it stands: how tall, and who is on which floor.</summary>
/// <param name="Floors">How many floors the tower is drawn with.</param>
/// <param name="Occupied">Every floor with a run on it, lowest first.</param>
public sealed record BuildingView(int Floors, IReadOnlyList<BuildingFloor> Occupied);

/// <summary>
/// Which run has which bays on which floor of the building, remembered from
/// one look to the next.
/// </summary>
/// <remarks>
/// <para>
/// A floor's team side is split into bays, three unless the rules say
/// otherwise, and a run has as many as its people need, side by side where it
/// can: a small team shares a floor with others, a big one takes a floor or
/// more. A run seen for the first time is given its bays at once, on the
/// lowest floor with that many free together.
/// </para>
/// <para>
/// After that it moves only after the rules' waits: a team that has outgrown
/// its bays takes another once it has been too big for
/// <see cref="OfficeMoves.SpillUpSeconds"/> - the bay beside its own where
/// that is free, else on the floor above, else the lowest with room - and one
/// that fits in fewer gives the last back once it has for
/// <see cref="OfficeMoves.GiveBackSeconds"/>. The long wait down and the short
/// one up are so that a run briefing and finishing workers does not bounce.
/// Nobody else's bays move when one team comes, grows or goes.
/// </para>
/// <para>
/// A finished run keeps its bays, lights out, for
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
        /// <summary>Where it is, in order: its first floor's bays first.</summary>
        public List<(int Floor, int Bay, int Bays)> Places { get; } = [];

        public int Bays => Places.Sum(place => place.Bays);

        public DateTimeOffset? OverSince { get; set; }

        public DateTimeOffset? UnderSince { get; set; }
    }

    /// <summary>Take in the runs as they are now, and say who is on which floor.</summary>
    /// <param name="runs">Every run the office could show.</param>
    /// <param name="capacity">How many people a bay seats, lead included.</param>
    /// <param name="rules">For the bays to a floor, the fewest floors and the waits.</param>
    /// <param name="now">The time now.</param>
    public BuildingView Update(IReadOnlyList<RunSummary> runs, int capacity, OfficeRules rules, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(rules);

        var moves = rules.Moves ?? new OfficeMoves();
        var seat = Math.Max(1, capacity);
        var perFloor = Math.Max(1, rules.Bays ?? OfficeRules.BayCount);

        lock (_gate)
        {
            // Who is still in the building: every run going, and every run
            // finished within the time its bays are kept for it.
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
                    Grow(tenancy, need, perFloor);

                    continue;
                }

                // A finished run's bays stay as they were until it is gone.
                if (!run.Running)
                {
                    tenancy.OverSince = null;
                    tenancy.UnderSince = null;

                    continue;
                }

                if (need > tenancy.Bays)
                {
                    tenancy.UnderSince = null;
                    tenancy.OverSince ??= now;

                    if (now - tenancy.OverSince.Value >= TimeSpan.FromSeconds(moves.SpillUpSeconds))
                    {
                        Grow(tenancy, need - tenancy.Bays, perFloor);
                        tenancy.OverSince = null;
                    }
                }
                else if (need < tenancy.Bays)
                {
                    tenancy.OverSince = null;
                    tenancy.UnderSince ??= now;

                    if (now - tenancy.UnderSince.Value >= TimeSpan.FromSeconds(moves.GiveBackSeconds))
                    {
                        Shrink(tenancy, need);
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
                var places = _tenants[run.RunId].Places;
                var here = Present(run);
                var state = State(run);
                var seated = 0;

                for (var part = 0; part < places.Count; part++)
                {
                    // Filled from the first place on: the lead and the first
                    // workers there, whoever does not fit after.
                    var (floor, bay, bays) = places[part];
                    var on = part == places.Count - 1
                        ? here.Skip(seated).ToList()
                        : here.Skip(seated).Take(seat * bays).ToList();

                    seated += on.Count;
                    occupied.Add(new BuildingFloor(floor, run.RunId, part, on.Count, state, !run.Running, on, bay, bays));
                }
            }

            occupied.Sort((a, b) => a.Number != b.Number ? a.Number.CompareTo(b.Number) : a.Bay.CompareTo(b.Bay));

            var tallest = occupied.Count == 0 ? 0 : occupied.Max(one => one.Number);

            return new BuildingView(Math.Max(rules.MinFloors ?? 10, tallest), occupied);
        }
    }

    /// <summary>
    /// More bays for a tenant: the one beside its last where that is free,
    /// else as many as fit together on the floor above its last, else on the
    /// lowest floor with room.
    /// </summary>
    private void Grow(Tenancy tenancy, int more, int perFloor)
    {
        while (more > 0)
        {
            if (tenancy.Places.Count > 0)
            {
                var (floor, bay, bays) = tenancy.Places[^1];

                if (bay + bays < perFloor && Free(floor, bay + bays))
                {
                    tenancy.Places[^1] = (floor, bay, bays + 1);
                    more--;

                    continue;
                }
            }

            var take = Math.Min(more, perFloor);
            var at = Room(tenancy, take, perFloor);

            tenancy.Places.Add((at.Floor, at.Bay, take));
            more -= take;
        }
    }

    /// <summary>Fewer bays: the last one given back first.</summary>
    private static void Shrink(Tenancy tenancy, int need)
    {
        while (tenancy.Bays > Math.Max(1, need))
        {
            var (floor, bay, bays) = tenancy.Places[^1];

            if (bays > 1)
            {
                tenancy.Places[^1] = (floor, bay, bays - 1);
            }
            else
            {
                tenancy.Places.RemoveAt(tenancy.Places.Count - 1);
            }
        }
    }

    // Whether nobody has this bay of this floor.
    private bool Free(int floor, int bay) =>
        !_tenants.Values.Any(one => one.Places.Any(place => place.Floor == floor && bay >= place.Bay && bay < place.Bay + place.Bays));

    /// <summary>Where a number of bays side by side are free: the floor above the tenant's last first, then the lowest.</summary>
    private (int Floor, int Bay) Room(Tenancy tenancy, int take, int perFloor)
    {
        (int Floor, int Bay)? On(int floor)
        {
            for (var bay = 0; bay + take <= perFloor; bay++)
            {
                if (Enumerable.Range(bay, take).All(one => Free(floor, one)))
                {
                    return (floor, bay);
                }
            }

            return null;
        }

        if (tenancy.Places.Count > 0 && On(tenancy.Places[^1].Floor + 1) is { } above)
        {
            return above;
        }

        for (var floor = 1; ; floor++)
        {
            if (On(floor) is { } found)
            {
                return found;
            }
        }
    }

    /// <summary>A run's people still in the building, the lead first as the floors seat them.</summary>
    private static List<string> Present(RunSummary run) =>
    [
        .. run.Nodes
            .Where(node => OfficeIntent.For(run, node).Place != OfficePlace.Gone)
            .OrderBy(node => node.Role == "role.project-lead" ? 0 : 1)
            .Select(node => node.Node),
    ];

    // Bays: one for every so many people, never none.
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

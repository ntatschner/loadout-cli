namespace Loadout.Platform.Abstractions;

/// <summary>One process as a snapshot saw it.</summary>
/// <param name="Id">Its identifier.</param>
/// <param name="Parent">The identifier of whatever started it, or 0 where there is none.</param>
/// <param name="ResidentBytes">Memory it holds resident, or null where that could not be read.</param>
public sealed record ProcessEntry(int Id, int Parent, long? ResidentBytes);

/// <summary>Every process on the machine, with its parent and memory, for measuring what a session costs.</summary>
public interface IProcessTree
{
    /// <summary>A snapshot of every process the platform will say anything about.</summary>
    Task<IReadOnlyList<ProcessEntry>> SnapshotAsync(CancellationToken ct = default);
}

/// <summary>Adding up a process and everything it started.</summary>
public static class ProcessTrees
{
    /// <summary>
    /// The resident memory of each root and its descendants, for the roots
    /// the snapshot has; a root it does not have is left out.
    /// </summary>
    /// <remarks>
    /// An estimate, and said to be one. On Windows a process keeps its
    /// parent's identifier after the parent has gone, and identifiers are
    /// reused, so now and then a process is counted under a root that did not
    /// start it. Each process is counted once whatever the table says, so a
    /// table with a cycle in it still ends.
    /// </remarks>
    public static IReadOnlyDictionary<int, long> Resident(IReadOnlyList<ProcessEntry> table, IEnumerable<int> roots)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(roots);

        var byId = new Dictionary<int, ProcessEntry>();
        var children = new Dictionary<int, List<int>>();

        foreach (var entry in table)
        {
            byId[entry.Id] = entry;

            if (entry.Parent != entry.Id)
            {
                if (!children.TryGetValue(entry.Parent, out var list))
                {
                    children[entry.Parent] = list = [];
                }

                list.Add(entry.Id);
            }
        }

        var totals = new Dictionary<int, long>();

        foreach (var root in roots.Distinct())
        {
            if (!byId.ContainsKey(root))
            {
                continue;
            }

            var seen = new HashSet<int> { root };
            var waiting = new Stack<int>([root]);
            long bytes = 0;

            while (waiting.TryPop(out var id))
            {
                bytes += byId.TryGetValue(id, out var entry) ? entry.ResidentBytes ?? 0 : 0;

                foreach (var child in children.GetValueOrDefault(id, []))
                {
                    if (seen.Add(child))
                    {
                        waiting.Push(child);
                    }
                }
            }

            totals[root] = bytes;
        }

        return totals;
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Loadout.Core.Teams;

/// <summary>
/// Which of an office kit's people draws each node of a run.
/// </summary>
/// <remarks>
/// <para>
/// Worked out here rather than in the page, because the name on the desk has to
/// fit the face behind it: a kit says whether each of its people is a woman, a
/// man or neither, and <see cref="DeskNames"/> picks a first name to match. The
/// page draws whoever it is told, so the two cannot disagree.
/// </para>
/// <para>
/// A node is drawn from its role's list in the kit's skins, or from "worker"
/// when its role has none. Its place in that list is its rank by name among the
/// run's nodes drawing from the same list, moved on by a number fixed by the
/// run: nobody in a run is drawn twice until everybody in the list has been,
/// the same node is the same person every time it is drawn, on its floor and on
/// the roof, and two runs do not start with the same people in the same order.
/// </para>
/// </remarks>
public static class OfficeCast
{
    /// <summary>The sheet that draws each node of a run, by node; empty when the kit has no cast.</summary>
    /// <param name="kit">The office kit in use.</param>
    /// <param name="runId">The run.</param>
    /// <param name="nodes">The run's nodes, each with its role as the run gives it ("role.project-lead").</param>
    public static IReadOnlyDictionary<string, string> For(OfficeKit kit, string runId, IEnumerable<(string Node, string Role)> nodes)
    {
        var drawn = new Dictionary<string, string>(StringComparer.Ordinal);

        if (kit.Skins is not { Count: > 0 } skins || kit.Sheets is not { Count: > 0 } sheets)
        {
            return drawn;
        }

        var start = Start(runId);

        // Nodes drawn from the same list, by the list: two roles that both fall
        // back on "worker" share it, and so share its people out between them.
        var lists = new List<(IReadOnlyList<string> List, List<string> Nodes)>();

        foreach (var (node, role) in nodes.OrderBy(one => one.Node, StringComparer.Ordinal))
        {
            if (ListFor(skins, role) is not { Count: > 0 } list)
            {
                continue;
            }

            var index = lists.FindIndex(one => ReferenceEquals(one.List, list));

            if (index < 0)
            {
                lists.Add((list, []));
                index = lists.Count - 1;
            }

            lists[index].Nodes.Add(node);
        }

        foreach (var (list, members) in lists)
        {
            for (var rank = 0; rank < members.Count; rank++)
            {
                var name = list[(int)((start + (uint)rank) % (uint)list.Count)];

                if (sheets.ContainsKey(name))
                {
                    drawn[members[rank]] = name;
                }
            }
        }

        return drawn;
    }

    /// <summary>Who a sheet draws, for the name on their desk; null when the kit does not say.</summary>
    public static string? GenderOf(OfficeKit kit, string? sheet) =>
        sheet is not null && kit.Sheets is { } sheets && sheets.TryGetValue(sheet, out var found) ? found.Gender : null;

    private static IReadOnlyList<string>? ListFor(IReadOnlyDictionary<string, IReadOnlyList<string>> skins, string role)
    {
        var name = role.StartsWith("role.", StringComparison.Ordinal) ? role["role.".Length..] : role;

        return skins.TryGetValue(name, out var list) ? list : skins.GetValueOrDefault("worker");
    }

    // A stable hash, as DeskNames uses: string.GetHashCode differs from one process to the next.
    private static uint Start(string runId) => BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(runId)), 0);
}

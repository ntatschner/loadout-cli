using System.Security.Cryptography;
using System.Text;

namespace Loadout.Core.Teams;

/// <summary>
/// A name for each node that somebody might actually say out loud.
/// </summary>
/// <remarks>
/// <para>
/// A node is called <c>implementer/1</c>, which says what it is and nothing
/// about which one it was. In a room with three of them, and in a machine with
/// four runs going, every office has an <c>implementer/1</c> in it and none of
/// them is distinguishable from the others in conversation: "the implementer
/// got it wrong" is a sentence about five different agents.
/// </para>
/// <para>
/// So each one also gets a person's name. "Marguerite kept refusing things" is
/// a thing somebody can say a week later, and it points at exactly one node of
/// exactly one run - which is the entire point, and the same argument
/// <see cref="RoomNames"/> makes for runs.
/// </para>
/// <para>
/// Worked out from the run and the node rather than stored, so the same node is
/// the same person on every machine that reads the journal, including one
/// reading a journal it did not write. Nothing to keep in step, nothing to
/// migrate, and the technical name is never replaced - it is what every
/// command still takes, and it stays on the page beside the person's.
/// </para>
/// </remarks>
public static class DeskNames
{
    /// <summary>
    /// First names, chosen to be short, plainly different from one another and
    /// from a broad spread of places.
    /// </summary>
    /// <remarks>
    /// Short because these sit on a name plate the width of a desk. Plainly
    /// different because two nodes called Sam and Sammy in one room is worse
    /// than numbering them.
    /// </remarks>
    private static readonly string[] Firsts =
    [
        "Ada", "Bex", "Cleo", "Dara", "Ebo", "Faye", "Gus", "Hana",
        "Ines", "Jae", "Kofi", "Lena", "Mo", "Nell", "Omar", "Pia",
        "Quinn", "Rhys", "Sena", "Tam", "Uma", "Vik", "Wren", "Xan",
        "Yuki", "Zane", "Arjun", "Bo", "Cai", "Dot", "Elif", "Fen",
        "Gita", "Huw", "Ida", "Juno", "Kit", "Liv", "Mina", "Niko",
        "Orla", "Priya", "Rune", "Suki", "Theo", "Ursa", "Vera", "Wes",
    ];

    /// <summary>
    /// Surnames, so two Adas in one office are still two people.
    /// </summary>
    /// <remarks>
    /// Only ever shown when the first name is not enough - on the plate there
    /// is room for one word, and in the detail there is room for both.
    /// </remarks>
    private static readonly string[] Lasts =
    [
        "Abara", "Bright", "Calder", "Duval", "Ellis", "Frost", "Gale", "Hale",
        "Ivers", "Jost", "Kerr", "Lowe", "Marsh", "Nardi", "Okafor", "Pike",
        "Quill", "Raines", "Soto", "Tan", "Ustinov", "Vance", "Wilde", "Yoshida",
    ];

    /// <summary>How many people this agency has on its books.</summary>
    /// <remarks>
    /// Two parts rather than one list, for the reason the rooms give: one list
    /// of four dozen repeats itself inside a single busy afternoon, and a name
    /// two nodes share is not a name.
    /// </remarks>
    public static int People => Firsts.Length * Lasts.Length;

    /// <summary>The person at a node's desk, first name only.</summary>
    /// <param name="runId">The run, so the same node of another run is somebody else.</param>
    /// <param name="node">The node, as the run named it.</param>
    public static string For(string? runId, string? node) => Both(runId, node).First;

    /// <summary>The person at a node's desk, in full.</summary>
    public static string Full(string? runId, string? node)
    {
        var (first, last) = Both(runId, node);

        return first.Length == 0 ? string.Empty : first + " " + last;
    }

    private static (string First, string Last) Both(string? runId, string? node)
    {
        if (string.IsNullOrWhiteSpace(node))
        {
            return (string.Empty, string.Empty);
        }

        // A stable hash rather than string.GetHashCode, which is randomised
        // per process: the same node would be a different person every time
        // the dashboard restarted, which is worse than having no name at all.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes((runId ?? string.Empty) + "/" + node));

        return (
            Firsts[BitConverter.ToUInt32(digest, 0) % (uint)Firsts.Length],
            Lasts[BitConverter.ToUInt32(digest, 4) % (uint)Lasts.Length]);
    }
}

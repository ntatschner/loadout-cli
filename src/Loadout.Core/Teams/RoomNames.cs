using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Loadout.Core.Teams;

/// <summary>
/// A name for each run that somebody might actually remember.
/// </summary>
/// <remarks>
/// <para>
/// A run is called 20260918-1436-ed59, which is precise, sortable and
/// impossible to hold in your head. A week later "the one where the reviewer
/// kept refusing things" is how anybody refers to it, and that has to attach
/// to something. So every run also gets a room, because the office view puts
/// each one in a room and a room with a name is a thing you can walk back
/// into.
/// </para>
/// <para>
/// Worked out from the identifier rather than stored, so the same run is the
/// same room on every machine that reads its journal, including one reading a
/// journal it did not write. Nothing to keep in step and nothing to migrate.
/// </para>
/// <para>
/// The names are meant to raise a smile. An office taken to its natural
/// conclusion is funnier than a word list, and a run somebody smiled at is a
/// run somebody remembers - which is the entire point of naming it.
/// </para>
/// </remarks>
public static class RoomNames
{
    private static readonly string[] Places =
    [
        "Broom Cupboard",
        "Quiet Carriage",
        "Second Best Boardroom",
        "Stationery Overflow",
        "Server Cupboard",
        "Phone Booth Two",
        "Mezzanine",
        "Long Table",
        "Sub-basement Filing",
        "Good Kettle",
        "Other Kitchen",
        "Wellness Room",
        "Fire Exit",
        "Loading Bay",
        "Atrium",
        "Breakout Space",
        "Hot Desk Twelve",
        "Print Room",
        "Reception",
        "Meeting Room 4B",
        "Corner Office",
        "Bike Shed",
        "Kitchenette",
        "Overflow Annexe",
    ];

    private static readonly string[] Qualifiers =
    [
        "Haunted",
        "Disputed",
        "Double Booked",
        "Under Repair",
        "Slightly Damp",
        "Recently Rebranded",
        "Awaiting Signage",
        "No Windows",
        "Too Warm",
        "Keycard Only",
        "Mind the Step",
        "Formerly the Gym",
        "Lift Out of Order",
        "Unreliable Wi-Fi",
        "Plant Died",
        "Chair Shortage",
    ];

    /// <summary>How many rooms this office has.</summary>
    /// <remarks>
    /// Two parts rather than one list, because one list of two dozen names
    /// repeats itself by the seventh run and a name two runs share is not a
    /// name. This is a few hundred, which is enough that a repeat is a
    /// coincidence somebody notices rather than a thing that keeps happening.
    /// </remarks>
    public static int Rooms => Places.Length * Qualifiers.Length;

    /// <summary>The room a run is in.</summary>
    /// <param name="runId">The run's identifier, as the journal names it.</param>
    public static string For(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return "The Corridor";
        }

        // A stable hash rather than string.GetHashCode, which is deliberately
        // randomised per process: the same run would be a different room every
        // time the dashboard restarted, which is worse than having no name.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(runId));

        var place = Places[BitConverter.ToUInt32(digest, 0) % (uint)Places.Length];
        var qualifier = Qualifiers[BitConverter.ToUInt32(digest, 4) % (uint)Qualifiers.Length];

        return string.Create(CultureInfo.InvariantCulture, $"The {place} ({qualifier})");
    }
}

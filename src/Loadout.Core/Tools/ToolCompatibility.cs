using Loadout.Models.Tools;

namespace Loadout.Core.Tools;

/// <summary>
/// Whether a version breaks the callers of the one before it, and whether it
/// has owned up to it.
/// </summary>
/// <remarks>
/// Computed rather than declared, because the agent writing a version is the
/// one with a reason to say it breaks nothing. Removing or renaming a required
/// input, adding one with no default, and changing what an exit code means all
/// break somebody's call that worked yesterday.
/// </remarks>
public static class ToolCompatibility
{
    /// <summary>How a version breaks its predecessor's callers. Empty when it does not.</summary>
    public static IReadOnlyList<string> Breaks(ToolVersion previous, ToolVersion next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);

        var found = new List<string>();
        var inputs = next.Inputs.ToDictionary(one => one.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var input in previous.Inputs.Where(one => one.Required))
        {
            if (!inputs.ContainsKey(input.Name))
            {
                found.Add($"The required input '{input.Name}' is gone.");
            }
        }

        var before = previous.Inputs.Select(one => one.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var input in next.Inputs.Where(one => one.Required && one.Default is null && !before.Contains(one.Name)))
        {
            found.Add($"'{input.Name}' is a new required input with no default.");
        }

        foreach (var (code, meaning) in previous.Outputs.Exit)
        {
            if (!next.Outputs.Exit.TryGetValue(code, out var now)
                || !string.Equals(now.Trim(), meaning.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                found.Add($"Exit code {code} no longer means '{meaning}'.");
            }
        }

        return found;
    }

    /// <summary>
    /// Why a version may not follow its predecessor, or null where it may.
    /// </summary>
    /// <remarks>
    /// A break is allowed only when declared, with a major bump and the words a
    /// caller needs to move over.
    /// </remarks>
    public static string? Refusal(ToolVersion previous, ToolVersion next)
    {
        var breaks = Breaks(previous, next);

        if (breaks.Count == 0 || Declared(previous, next))
        {
            return null;
        }

        return "This version breaks callers of " + previous.Version + " without saying so: "
            + string.Join(" ", breaks)
            + " Declare compatibility.breaks, bump the major version and write the migration.";
    }

    /// <summary>Whether a break has been declared the only way that counts.</summary>
    public static bool Declared(ToolVersion previous, ToolVersion next) =>
        next.Compatibility.Breaks
        && Major(next.Version) > Major(previous.Version)
        && next.Compatibility.Migration is { Length: > 0 } said
        && !string.IsNullOrWhiteSpace(said);

    /// <summary>The major part of <c>major.minor</c>, or -1 where it is not a number.</summary>
    public static int Major(string? version) =>
        int.TryParse((version ?? string.Empty).Split('.')[0], out var major) ? major : -1;
}

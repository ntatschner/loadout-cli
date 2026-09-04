using System.Text.RegularExpressions;

namespace Loadout.Core.Checkpoints;

/// <summary>
/// What a checkpoint may be called.
/// </summary>
/// <remarks>
/// The name becomes a filename, so it is checked rather than trusted. A name
/// carrying a separator would write outside the directory it belongs in, and
/// "before the refactor/v2" is the sort of thing somebody types without
/// thinking of it as a path at all.
/// </remarks>
public static partial class CheckpointNames
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled)]
    private static partial Regex Allowed();

    /// <summary>Whether a name is usable, and why not when it is not.</summary>
    public static string? Rejection(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "A checkpoint needs a name.";
        }

        var trimmed = name.Trim();

        if (!Allowed().IsMatch(trimmed))
        {
            return $"'{trimmed}' cannot be a checkpoint name. Use letters, digits, dots, "
                + "dashes and underscores, starting with a letter or digit, up to 64 characters.";
        }

        return null;
    }
}

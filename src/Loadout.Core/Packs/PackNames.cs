using System.Text.RegularExpressions;

namespace Loadout.Core.Packs;

/// <summary>
/// What a pack may be called.
/// </summary>
/// <remarks>
/// The name becomes a directory under the state root, so it is checked rather
/// than trusted — the same shape checkpoints and tasks use, for the same reason.
/// A name that can carry a separator is a name that can be pointed at a path,
/// and this one arrives from a file somebody else may have written.
/// </remarks>
public static partial class PackNames
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,47}$", RegexOptions.Compiled)]
    private static partial Regex Allowed();

    /// <summary>Why a name cannot be used, or null when it can.</summary>
    public static string? Rejection(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "A pack needs a name.";
        }

        return Allowed().IsMatch(name.Trim())
            ? null
            : $"'{name.Trim()}' cannot be a pack name. Use letters, digits, dots, dashes and "
                + "underscores, starting with a letter or digit, up to 48 characters.";
    }
}

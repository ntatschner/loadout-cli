using System.Text.RegularExpressions;

namespace Loadout.Core.Tasks;

/// <summary>
/// What a task may be called.
/// </summary>
/// <remarks>
/// Short and typeable, because these are quoted in conversation and on a
/// command line far more often than they are read from a list. The shape is the
/// same one checkpoints use and for the same reason: an identifier that can
/// carry a separator is one that can be pointed at a path.
/// </remarks>
public static partial class TaskIds
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,47}$", RegexOptions.Compiled)]
    private static partial Regex Allowed();

    /// <summary>Why an id cannot be used, or null when it can.</summary>
    public static string? Rejection(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "A task needs an id.";
        }

        return Allowed().IsMatch(id.Trim())
            ? null
            : $"'{id.Trim()}' cannot be a task id. Use letters, digits, dots, dashes and "
                + "underscores, starting with a letter or digit, up to 48 characters.";
    }
}

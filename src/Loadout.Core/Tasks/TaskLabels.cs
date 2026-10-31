using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>What a label may be, and how labels are compared.</summary>
/// <remarks>
/// Labels are the person's own words, so they are kept as written and compared
/// without regard to case: <c>Homelab</c> and <c>homelab</c> are one label, and
/// whichever was written first is the one kept.
/// </remarks>
public static class TaskLabels
{
    /// <summary>The longest a label may be: a word or two, not a sentence.</summary>
    public const int Length = 32;

    /// <summary>How two labels are compared.</summary>
    public static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    /// <summary>Why this cannot be a label, or null when it can.</summary>
    public static string? Rejection(string? label)
    {
        var trimmed = label?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return "A label needs a word in it.";
        }

        if (trimmed.Length > Length)
        {
            return $"'{trimmed}' is {trimmed.Length} characters, and a label is at most {Length}: a word or two.";
        }

        if (trimmed.Any(char.IsControl) || trimmed.Contains(','))
        {
            return $"'{trimmed}' has a comma or a line break in it, which a label cannot: give each label on its own.";
        }

        // A label spelt like a type would read as one in every list that showed
        // it, while triggering nothing a type does.
        return TaskTypes.Parse(trimmed) is { } type
            ? $"'{trimmed}' is a type. Give it with --type, or choose another word for the label."
            : null;
    }

    /// <summary>
    /// The labels after adding some and taking some off: added ones not already
    /// there go on the end, and a label taken off goes whatever its case.
    /// </summary>
    public static List<string> Apply(IEnumerable<string> labels, IEnumerable<string>? add, IEnumerable<string>? remove)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var gone = new HashSet<string>((remove ?? []).Select(one => one.Trim()), Same);
        var kept = labels.Where(one => !gone.Contains(one)).ToList();

        foreach (var one in (add ?? []).Select(one => one.Trim()))
        {
            if (one.Length > 0 && !kept.Contains(one, Same))
            {
                kept.Add(one);
            }
        }

        return kept;
    }
}

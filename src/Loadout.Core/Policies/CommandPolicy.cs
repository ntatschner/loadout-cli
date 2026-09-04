namespace Loadout.Core.Policies;

/// <summary>What an agent may and may not be allowed to run, once both halves are in.</summary>
/// <param name="Denied">Commands the project forbids.</param>
/// <param name="PreApproved">
/// Commands this machine will not be asked about, with anything the project
/// denied already removed.
/// </param>
/// <param name="Overruled">
/// Pre-approvals dropped because the project denied them. Reported rather than
/// discarded: somebody who put a command in their own configuration and never
/// sees it take effect is owed the reason.
/// </param>
public sealed record ResolvedCommandPolicy(
    IReadOnlyList<string> Denied,
    IReadOnlyList<string> PreApproved,
    IReadOnlyList<string> Overruled);

/// <summary>
/// Combines the project's denials with this machine's pre-approvals.
/// </summary>
/// <remarks>
/// <para>
/// The two halves come from deliberately different places. Denial is shared,
/// because tightening is safe to hand somebody. Pre-approval is machine-local,
/// because it removes an approval prompt and nobody should be able to remove
/// one on a colleague's machine by committing a file.
/// </para>
/// <para>
/// Where they meet, denial wins. Any other rule would make the shared half
/// advisory, and a security control that a local file can switch off is not one.
/// </para>
/// </remarks>
public static class CommandPolicy
{
    /// <summary>Applies the denials to the pre-approvals.</summary>
    public static ResolvedCommandPolicy Resolve(
        IReadOnlyList<string>? denied,
        IReadOnlyList<string>? preApproved)
    {
        var denials = Clean(denied);
        var approvals = Clean(preApproved);

        var kept = new List<string>();
        var overruled = new List<string>();

        foreach (var approval in approvals)
        {
            if (denials.Any(denial => Covers(denial, approval)))
            {
                overruled.Add(approval);
            }
            else
            {
                kept.Add(approval);
            }
        }

        return new ResolvedCommandPolicy(denials, kept, overruled);
    }

    /// <summary>
    /// Whether a denial covers a command.
    /// </summary>
    /// <remarks>
    /// On whole words, so denying <c>git push</c> also stops
    /// <c>git push --force</c> and denying <c>git</c> stops all of it, while
    /// denying <c>rm</c> leaves <c>rmdir</c> alone. Prefix matching on raw
    /// characters would do the first two and get the third wrong, which is the
    /// kind of wrong that only shows up as a command mysteriously refused.
    /// </remarks>
    internal static bool Covers(string denial, string command) =>
        command.Equals(denial, StringComparison.OrdinalIgnoreCase)
        || command.StartsWith(denial + " ", StringComparison.OrdinalIgnoreCase);

    private static List<string> Clean(IReadOnlyList<string>? entries)
    {
        if (entries is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<string>();

        foreach (var entry in entries)
        {
            // Collapsed so that "git  push" and "git push" are the same denial.
            // A rule that can be evaded by typing two spaces is not a rule.
            var trimmed = string.Join(' ', entry.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                cleaned.Add(trimmed);
            }
        }

        return cleaned;
    }
}

namespace Loadout.Core.Workspace;

/// <summary>
/// What may be named as a thing to share.
/// </summary>
/// <remarks>
/// The path arrives from a command line and is joined to the workspace root, so
/// it is checked rather than trusted. Rejecting the obvious shapes here is not
/// the whole defence — where the path finally resolves is checked as well,
/// because a path that looks harmless can still land somewhere else.
/// </remarks>
public static class SharePaths
{
    /// <summary>Why a path cannot be used, or null when it can.</summary>
    public static string? Rejection(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Name the file to move, as 'share candidates' printed it.";
        }

        var tidied = path.Replace('\\', '/').Trim();

        if (tidied.Contains("..", StringComparison.Ordinal))
        {
            return "A path to share cannot climb out of the workspace.";
        }

        if (tidied.StartsWith('/') || (tidied.Length > 1 && tidied[1] == ':'))
        {
            return "Name the file relative to the workspace, not as an absolute path.";
        }

        // The private half, refused by name as well as by not being searched.
        // Somebody can type a path the candidate search would never have
        // offered, and handoffs are the reason a workspace is created private.
        foreach (var forbidden in ShareCandidates.NeverSearched)
        {
            if (tidied.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                return $"'{forbidden.TrimEnd('/')}' is not shareable. A workspace holds handoffs, "
                    + "memory and decisions, and putting those in front of everybody who clones "
                    + "it cannot be taken back.";
            }
        }

        return null;
    }
}

/// <summary>
/// Whether a file's contents may go into a layer everybody sees.
/// </summary>
/// <remarks>
/// In Core rather than at the call site, so that every way of promoting
/// something is screened rather than the one somebody remembered. The global
/// layer is pulled by everybody who clones the workspace, and a pull cannot be
/// taken back.
/// </remarks>
public static class SharedContent
{
    /// <summary>Why this text must not be shared, or null when it may be.</summary>
    /// <remarks>
    /// Names the pattern and never the value. A refusal that quoted what it
    /// found would put the credential into terminal scrollback and logs, which
    /// is the problem rather than the report of it.
    /// </remarks>
    public static string? Refusal(string? text)
    {
        var patterns = Security.SecretScanner.Match(text);

        return patterns.Count == 0
            ? null
            : $"it contains something shaped like a credential ({string.Join(", ", patterns)}). "
                + "Take the value out first — what goes into the global layer is seen by "
                + "everybody who clones this workspace.";
    }
}

using Loadout.Core.Git;

namespace Loadout.Core.Triggers;

/// <summary>Names a repository on GitHub as <c>owner/name</c>, the form its API takes.</summary>
public static class GitHubRepo
{
    /// <summary>The repository a project's remote points at, or null where it is not on github.com.</summary>
    public static string? From(string? remote)
    {
        if (GitRemote.Canonicalise(remote) is not { } canonical
            || !canonical.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Parse(canonical["github.com/".Length..]);
    }

    /// <summary>The repository written as <c>owner/name</c>, or null where it is not one.</summary>
    public static string? Parse(string? text)
    {
        var parts = (text ?? string.Empty).Trim().Split('/');

        return parts.Length == 2 && parts.All(Valid) ? $"{parts[0]}/{parts[1]}" : null;
    }

    private static bool Valid(string part) =>
        part.Length > 0 && part.All(one => char.IsAsciiLetterOrDigit(one) || one is '-' or '_' or '.');
}

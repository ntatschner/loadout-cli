using System.Text.RegularExpressions;
using Loadout.Core.Security;
using Loadout.Models.Tools;

namespace Loadout.Core.Tools;

/// <summary>
/// Whether a tool still carries the project it was written for.
/// </summary>
/// <remarks>
/// <para>
/// A tool is shared by every team on this machine. A script with somebody's
/// drive, repository or address in it works for the project it came from and
/// quietly does the wrong thing everywhere else, so those values have to become
/// inputs before anything is promoted.
/// </para>
/// <para>
/// Pattern-based, and the limit is stated: a project detail with no
/// recognisable shape passes unless it is a known project or team name. A
/// secret is reported by the name of its pattern and never by its value, for
/// the reason <see cref="SecretScanner" /> gives.
/// </para>
/// </remarks>
public static partial class ToolGenericity
{
    /// <summary>
    /// What in the text ties it to one project, one finding per line. Empty
    /// when it reads as generic.
    /// </summary>
    /// <param name="text">What to check.</param>
    /// <param name="known">Project slugs and team names this machine knows.</param>
    public static IReadOnlyList<string> Check(string? text, IEnumerable<string>? known = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var found = new List<string>();
        var secrets = SecretScanner.Match(text);

        // Secrets first, and by type only.
        foreach (var name in secrets)
        {
            found.Add($"a credential ({name})");
        }

        // Once anything in the text is a credential, no value is quoted at
        // all: a path, URL or address can carry it, and a pattern only has to
        // miss one shape of token for the refusal to print it.
        var quote = secrets.Count == 0;

        Look(found, "an absolute path", WindowsPath(), text, quote);
        Look(found, "an absolute path", UnixPath(), text, quote);
        Look(found, "a repository URL", RepositoryUrl(), text, quote);
        Look(found, "an e-mail address", Email(), text, quote);
        Look(found, "a GUID", Guid(), text, quote);

        foreach (var name in (known ?? []).Where(one => one is { Length: >= 3 }).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Regex.IsMatch(
                text,
                $@"(?<![A-Za-z0-9_-]){Regex.Escape(name)}(?![A-Za-z0-9_-])",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1)))
            {
                found.Add($"a project or team name: '{name}'");
            }
        }

        return found;
    }

    /// <summary>Everything about a version that is shared: its manifest, script and cases.</summary>
    public static IReadOnlyList<string> Check(
        ToolVersion version,
        string script,
        IEnumerable<ToolCase> cases,
        IEnumerable<string>? known = null)
    {
        ArgumentNullException.ThrowIfNull(version);

        var parts = new List<string?>
        {
            version.Purpose,
            version.Origin,
            version.ErrorBehaviour,
            version.Outputs.Stdout,
            script,
        };

        parts.AddRange(version.Constraints);
        parts.AddRange(version.Dependencies);
        parts.AddRange(version.Examples.SelectMany(one => new[] { one.Command, one.Expect }));
        parts.AddRange(version.Inputs.SelectMany(one => new[] { one.Default, one.Describe }));
        parts.AddRange(cases.SelectMany(one => one.Args.Values.Concat(one.Setup.Files.Keys)));

        return [.. Check(string.Join('\n', parts.Where(one => one is { Length: > 0 })), known).Distinct()];
    }

    private static void Look(List<string> found, string what, Regex pattern, string text, bool quote)
    {
        try
        {
            foreach (Match match in pattern.Matches(text))
            {
                // A URL's user-info is a name and a password, and neither is
                // anybody else's business, whether or not a pattern knew it.
                var value = UserInfo().Replace(match.Value.Trim(), "://");

                found.Add(quote && SecretScanner.Match(value).Count == 0 ? $"{what}: '{value}'" : $"{what} (not quoted)");
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Unchecked is not the same as clean.
            found.Add($"{what} (the check did not complete)");
        }
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/][^\s'""]*|\\\\[A-Za-z0-9.-]+\\[^\s'""]+", RegexOptions.None, 1000)]
    private static partial Regex WindowsPath();

    [GeneratedRegex(@"(?<![\w.{}~/-])/(?:home|Users|mnt|opt|var|srv|root|media|Volumes|etc|usr|tmp)/[^\s'""]*", RegexOptions.None, 1000)]
    private static partial Regex UnixPath();

    [GeneratedRegex(
        @"\b(?:https?|ssh|git)://(?:[^\s'""@]+@)?(?:github\.com|gitlab\.com|bitbucket\.org|dev\.azure\.com|ssh\.dev\.azure\.com)/[^\s'""]*|\b(?:https?|ssh|git)://[^\s'""]+\.git\b|\bgit@[A-Za-z0-9.-]+:[^\s'""]+",
        RegexOptions.IgnoreCase,
        1000)]
    private static partial Regex RepositoryUrl();

    [GeneratedRegex(@"://[^\s'""/@]+@", RegexOptions.None, 1000)]
    private static partial Regex UserInfo();

    // Not after ':' or '/', so a URL's password is never read as the start of an address.
    [GeneratedRegex(@"(?<![\w.%+:/-])[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}\b", RegexOptions.None, 1000)]
    private static partial Regex Email();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.None, 1000)]
    private static partial Regex Guid();
}

using System.Text.RegularExpressions;

namespace AgentWorkspace.Core.Git;

/// <summary>
/// Canonicalises Git remote URLs so that the several ways of writing one
/// repository resolve to a single identity (spec section 29).
/// <para>
/// This matters because the same repository is routinely referred to as
/// <c>git@host:group/repo.git</c> by one machine, <c>ssh://git@host/group/repo</c>
/// by another and <c>https://host/group/repo.git</c> by a third. Without
/// canonicalisation the launcher would register one project three times.
/// </para>
/// </summary>
public static partial class GitRemote
{
    /// <summary>
    /// Reduces a remote URL to a comparable key of the form <c>host/path</c>.
    /// Returns null for input that is not a recognisable remote.
    /// </summary>
    /// <remarks>
    /// The scheme, any user component and any port are dropped. Dropping the
    /// port is a deliberate trade: one server reached over a non-default SSH
    /// port in one config and the default in another is the same repository,
    /// and treating those as distinct projects is a worse failure than the
    /// theoretical case of two different repositories differing only by port.
    /// <para>
    /// The host is lower-cased because DNS is case-insensitive. The path is
    /// not, because many Git hosts are case-sensitive about repository paths.
    /// </para>
    /// </remarks>
    public static string? Canonicalise(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote))
        {
            return null;
        }

        var trimmed = remote.Trim();

        var scpMatch = ScpLikeSyntax().Match(trimmed);
        if (scpMatch.Success)
        {
            return Compose(
                scpMatch.Groups["host"].Value,
                scpMatch.Groups["path"].Value);
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            // A local path parses as a file URI; its identity is the path alone.
            if (uri.IsFile)
            {
                return NormalisePath(uri.LocalPath);
            }

            return Compose(uri.Host, uri.AbsolutePath);
        }

        // A bare local path, relative or absolute, is a valid Git remote.
        return NormalisePath(trimmed);
    }

    /// <summary>Whether two remote URLs refer to the same repository.</summary>
    public static bool AreEquivalent(string? left, string? right)
    {
        var a = Canonicalise(left);
        var b = Canonicalise(right);

        return a is not null && b is not null && string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the repository name from a remote, for suggesting a project
    /// slug. Returns null when no sensible name can be derived.
    /// </summary>
    public static string? InferRepositoryName(string? remote)
    {
        var canonical = Canonicalise(remote);
        if (canonical is null)
        {
            return null;
        }

        var lastSegment = canonical.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

        return string.IsNullOrWhiteSpace(lastSegment) ? null : lastSegment;
    }

    private static string Compose(string host, string path)
    {
        var normalisedHost = host.ToLowerInvariant();
        var normalisedPath = NormalisePath(path);

        return normalisedHost.Length == 0
            ? normalisedPath
            : normalisedHost + "/" + normalisedPath;
    }

    private static string NormalisePath(string path)
    {
        // Backslashes appear in Windows-style local remotes; they are separators
        // here, not literal characters.
        var value = path.Replace('\\', '/').Trim('/');

        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        return value.Trim('/');
    }

    /// <summary>
    /// The scp-like form Git accepts, e.g. <c>git@host:group/repo.git</c>.
    /// A colon separates host from path, and the path is not absolute, which is
    /// what distinguishes it from a URL with a port.
    /// </summary>
    [GeneratedRegex(@"^(?:(?<user>[^@/\\]+)@)?(?<host>[^:/\\]+):(?!//)(?<path>.+)$",
        RegexOptions.None, 1000)]
    private static partial Regex ScpLikeSyntax();
}

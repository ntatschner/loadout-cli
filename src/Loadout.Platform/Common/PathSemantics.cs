using System.Collections.Concurrent;
using System.Text;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Common;

/// <summary>
/// Path comparison that asks the filesystem what it actually does instead of
/// inferring it from the operating system (spec section 84).
/// <para>
/// The inference is wrong often enough to matter. APFS ships in both
/// case-sensitive and case-insensitive forms, so "macOS is case-insensitive"
/// is false on a developer who formatted their volume that way. NTFS carries a
/// per-directory case-sensitivity flag, so the answer can differ between two
/// directories on one Windows volume. Getting it wrong registers one clone as
/// two projects, or silently merges two distinct ones.
/// </para>
/// </summary>
public sealed class PathSemantics : IPathSemantics
{
    // Cached per directory rather than per volume. That is not over-caution:
    // NTFS case sensitivity is genuinely a per-directory attribute, so a
    // volume-level cache would return the wrong answer for a flagged subtree.
    private readonly ConcurrentDictionary<string, bool> _caseInsensitivityCache =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool PathsEqual(string left, string right) =>
        string.Equals(Canonicalise(left), Canonicalise(right), StringComparison.Ordinal);

    /// <inheritdoc />
    public string Canonicalise(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        full = ResolveLinks(full);
        full = TrimTrailingSeparator(full);

        // macOS has historically stored filenames decomposed, so the same
        // visible name can arrive as NFD from a directory listing and NFC from
        // a config file. Normalising both sides is what stops those comparing
        // unequal.
        full = full.Normalize(NormalizationForm.FormC);

        return IsCaseInsensitive(full) ? full.ToLowerInvariant() : full;
    }

    /// <inheritdoc />
    public bool IsCaseInsensitive(string path)
    {
        var probeDirectory = FindNearestExistingDirectory(path);

        if (probeDirectory is null)
        {
            // Nothing on this path exists yet, so there is nothing to probe.
            // Fall back to the platform's usual behaviour and let the caller
            // re-ask once the directory is created.
            return DefaultForPlatform();
        }

        return _caseInsensitivityCache.GetOrAdd(probeDirectory, Probe);
    }

    private static bool DefaultForPlatform() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>
    /// Determines case sensitivity by observation. Prefers a read-only probe
    /// against an entry that already exists, because the launcher must work
    /// against read-only mounts and directories it cannot write to.
    /// </summary>
    private static bool Probe(string directory)
    {
        try
        {
            var readOnlyAnswer = ProbeUsingExistingEntry(directory);
            if (readOnlyAnswer is not null)
            {
                return readOnlyAnswer.Value;
            }

            return ProbeByWriting(directory) ?? DefaultForPlatform();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DefaultForPlatform();
        }
    }

    private static bool? ProbeUsingExistingEntry(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(entry);
            var flipped = FlipCase(name);

            // A name with no cased letters tells us nothing; keep looking.
            if (flipped is null)
            {
                continue;
            }

            var flippedPath = Path.Combine(directory, flipped);
            return File.Exists(flippedPath) || Directory.Exists(flippedPath);
        }

        return null;
    }

    private static bool? ProbeByWriting(string directory)
    {
        var stem = $".loadout-case-probe-{Guid.NewGuid():N}";
        var upper = Path.Combine(directory, stem.ToUpperInvariant());
        var lower = Path.Combine(directory, stem.ToLowerInvariant());

        try
        {
            using (File.Create(upper))
            {
            }

            return File.Exists(lower);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            TryDelete(upper);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover probe file is harmless; failing the caller is not.
        }
    }

    /// <summary>Returns the name with its case inverted, or null when it has no cased letters.</summary>
    private static string? FlipCase(string name)
    {
        var builder = new StringBuilder(name.Length);
        var changed = false;

        foreach (var c in name)
        {
            if (char.IsUpper(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                changed = true;
            }
            else if (char.IsLower(c))
            {
                builder.Append(char.ToUpperInvariant(c));
                changed = true;
            }
            else
            {
                builder.Append(c);
            }
        }

        return changed ? builder.ToString() : null;
    }

    private static string? FindNearestExistingDirectory(string path)
    {
        var candidate = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path));

        while (!string.IsNullOrEmpty(candidate))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.GetDirectoryName(candidate);
        }

        return null;
    }

    /// <summary>
    /// How many links deep to follow before giving up. A cycle is the reason
    /// for a limit rather than a count: following one forever would hang a
    /// comparison, and no real tree is nested this far.
    /// </summary>
    private const int MaximumLinkDepth = 40;

    /// <summary>
    /// Resolves symbolic links to their final target so that two routes to one
    /// repository compare equal. Spec section 84 calls this out for macOS and
    /// Linux, and Windows junctions behave the same way.
    /// <para>
    /// Every component is resolved, not only the last one. macOS makes that
    /// unavoidable: <c>/var</c> is a link to <c>/private/var</c>, so every
    /// temporary path has a symlinked ancestor and a leaf that is not a link at
    /// all. Resolving the leaf alone left two names for one directory, and
    /// project identity is built on these comparing equal.
    /// </para>
    /// </summary>
    private static string ResolveLinks(string path)
    {
        try
        {
            return Walk(path, MaximumLinkDepth);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A broken or circular link resolves to itself rather than failing
            // the whole comparison.
            return path;
        }
    }

    /// <summary>
    /// Resolves a path by resolving its parent first, then its own last
    /// component against the resolved parent.
    /// </summary>
    private static string Walk(string path, int budget)
    {
        if (budget <= 0)
        {
            return path;
        }

        var parent = Path.GetDirectoryName(path);

        // The volume root has no parent to resolve against.
        if (string.IsNullOrEmpty(parent))
        {
            return path;
        }

        var resolvedParent = Walk(parent, budget - 1);

        var candidate = Path.Combine(resolvedParent, Path.GetFileName(path));

        var target = Directory.Exists(candidate)
            ? Directory.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName
            : File.Exists(candidate)
                ? File.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName
                : null;

        if (target is null)
        {
            return candidate;
        }

        // A link can point at a relative path, and it can point at another
        // link, so the target is resolved in turn rather than trusted.
        return Walk(
            Path.IsPathRooted(target) ? target : Path.Combine(resolvedParent, target),
            budget - 1);
    }

    private static string TrimTrailingSeparator(string path)
    {
        // The volume root is its own terminator and must keep its separator.
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && string.Equals(path, root, StringComparison.Ordinal))
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

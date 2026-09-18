using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams;

/// <summary>
/// The art the office view draws with, installed on this machine rather than
/// shipped with Loadout.
/// </summary>
/// <remarks>
/// <para>
/// The office was built shape-first on purpose: every desk is a real element
/// carrying its name and its state as words, and the sprite is an empty square
/// on top of that. This is where the square stops being empty.
/// </para>
/// <para>
/// <strong>Nothing here is in the repository or the installer.</strong> Pixel
/// art asset packs are sold under licences that permit using the files inside a
/// finished project and forbid redistributing the originals, and a public
/// source repository redistributes everything in it to anybody who clones. So
/// the art lives in a directory on the machine that bought it, Loadout ships
/// none of it, and a build with no art draws exactly what it drew before.
/// </para>
/// <para>
/// A set is a directory; a piece is a file in it. Which sets exist is whatever
/// somebody put there, deliberately, rather than a list in the code: a list
/// would be a second place to keep in step with a folder, and the folder wins
/// every time.
/// </para>
/// </remarks>
public static class OfficeArt
{
    /// <summary>Where sets live: one directory per set.</summary>
    public static string Root(IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return Path.Combine(paths.Paths.State, "teams", "office");
    }

    /// <summary>
    /// The set to draw with and where it lives, or nothing to draw with.
    /// </summary>
    /// <remarks>
    /// A configured name that no directory answers to comes back as nothing,
    /// rather than as a set that serves 404s for every piece. The difference
    /// matters to whoever is reading the page: an office drawn as squares is
    /// what an unconfigured Loadout looks like, and it should also be what a
    /// misspelt one looks like, rather than something subtly broken.
    /// </remarks>
    public static (string? Root, string Set) Chosen(IPlatformPaths paths, string? set)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (set is not { Length: > 0 } wanted || !Names(wanted))
        {
            return (null, string.Empty);
        }

        var root = Root(paths);

        return Directory.Exists(Path.Combine(root, wanted))
            ? (root, wanted)
            : (null, string.Empty);
    }

    /// <summary>
    /// Whether that is the name of a set or a piece, as opposed to a path.
    /// </summary>
    /// <remarks>
    /// The same rule and the same reason as a run identifier: this name becomes
    /// part of a path, it arrives from a browser, and a name carrying a
    /// separator or a <c>..</c> becomes a file somewhere else entirely. Letters,
    /// digits, dash, underscore and a single dot before the extension. Never a
    /// leading dot, which is how a name becomes <c>..</c> in the first place.
    /// </remarks>
    public static bool Names(string? name) =>
        name is { Length: > 0 and <= 64 }
        && name[0] != '.'
        && name.All(one => char.IsAsciiLetterOrDigit(one) || one is '-' or '_' or '.')
        && !name.Contains("..", StringComparison.Ordinal);

    /// <summary>What a browser may be sent, by extension, and nothing else.</summary>
    /// <remarks>
    /// An allowed list rather than a guess. The directory is the person's own,
    /// but a set unzipped from somewhere carries whatever it carries, and the
    /// answer to a request for one of those is a refusal rather than a content
    /// type invented on the spot.
    /// </remarks>
    public static string? TypeOf(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => null,
        };

    /// <summary>The sets installed, in the order somebody reading them expects.</summary>
    public static IReadOnlyList<string> Sets(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory.EnumerateDirectories(root)
                    .Select(Path.GetFileName)
                    .Where(name => Names(name))
                    .OrderBy(name => name, StringComparer.Ordinal)!,
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The pieces in one set: the file names, without their extensions.
    /// </summary>
    /// <remarks>
    /// Without extensions because the page asks for a piece by what it is - the
    /// role it draws - and should not have to know whether whoever installed it
    /// saved a png or a webp.
    /// </remarks>
    public static IReadOnlyList<string> Pieces(string root, string set)
    {
        if (!Names(set))
        {
            return [];
        }

        var directory = Path.Combine(root, set);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory.EnumerateFiles(directory)
                    .Select(Path.GetFileName)
                    .Where(name => Names(name) && TypeOf(name!) is not null)
                    .Select(name => Path.GetFileNameWithoutExtension(name)!)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The file for one piece of one set, or null when there is not one.
    /// </summary>
    /// <remarks>
    /// Both names are checked before either is joined to a path, and the
    /// result is checked to be under the set's own directory afterwards. Twice,
    /// because the first is a rule about names and the second is a fact about
    /// the path that came out, and only the second survives somebody adding a
    /// character to the rule.
    /// </remarks>
    public static string? FileOf(string root, string set, string piece)
    {
        if (!Names(set) || !Names(piece))
        {
            return null;
        }

        var directory = Path.Combine(root, set);

        // Asked for with an extension, or by name and taken as found.
        var candidates = TypeOf(piece) is not null
            ? new[] { piece }
            : [piece + ".png", piece + ".webp", piece + ".gif"];

        foreach (var name in candidates)
        {
            var full = Path.GetFullPath(Path.Combine(directory, name));

            if (!full.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            if (File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }
}

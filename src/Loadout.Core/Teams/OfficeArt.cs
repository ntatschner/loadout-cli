using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams;

/// <summary>
/// The art the office view draws with, installed on this machine rather than
/// shipped with Loadout.
/// </summary>
/// <remarks>
/// <para>
/// The building is drawn from a set that is a kit, and in its own shapes where
/// there is none. A painted room, from before the building, is a set too: its
/// files are left where they are, and nothing draws them any more.
/// </para>
/// <para>
/// <strong>One set ships with Loadout: <see cref="BuiltIn"/>, the Tech
/// office</strong>, made for it and so Loadout's to give away. It is embedded
/// in the build and unpacked into the office folder, where it is drawn unless
/// another set is chosen. Anything else is somebody's own: asset packs are sold
/// under licences that allow using the files in a finished project and forbid
/// redistributing the originals, and a public source repository redistributes
/// everything in it, so those live on the machine that bought them and nowhere
/// else.
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
    /// <summary>The set Loadout ships, which the building is drawn from unless another is chosen.</summary>
    public const string BuiltIn = "loadout-tech";

    // In the built-in set's folder, saying which art it holds: a folder of that
    // name without one is somebody's own and is never written over, and one
    // holding older art is refreshed. A leading dot, so no set or piece name
    // can reach it.
    private const string Marker = ".loadout-built-in";

    private const string Resources = "office/" + BuiltIn + "/";

    /// <summary>
    /// Puts the built-in set in the office folder, or brings it up to date,
    /// and says whether it is there to draw from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unpacked rather than read from the build where it lies, so the set is an
    /// ordinary directory to everything that reads one - the kit check, the
    /// picture route, the stamps - and nothing needs to know it is special.
    /// </para>
    /// <para>
    /// Written over only where it says it is the built-in set: a folder called
    /// <see cref="BuiltIn"/> that somebody made themselves is left alone, and
    /// then the built-in set is not offered. Emptied before it is written, the
    /// marker last of all, so a picture the newer art dropped is not left
    /// behind, and an unpacking cut short is finished at the next start rather
    /// than taken for somebody's own.
    /// </para>
    /// </remarks>
    public static bool Unpack(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var assembly = typeof(OfficeArt).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Resources, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
        {
            return false;
        }

        var directory = Path.Combine(root, BuiltIn);
        var marker = Path.Combine(directory, Marker);

        try
        {
            var stamp = ArtStamp(assembly, names);

            if (Directory.Exists(directory))
            {
                if (!File.Exists(marker))
                {
                    return false;
                }

                if (File.ReadAllText(marker).Trim() == stamp)
                {
                    return true;
                }

                foreach (var file in Directory.EnumerateFiles(directory).Where(one => Path.GetFileName(one) != Marker))
                {
                    File.Delete(file);
                }
            }

            Directory.CreateDirectory(directory);

            if (!File.Exists(marker))
            {
                File.WriteAllText(marker, string.Empty);
            }

            foreach (var name in names)
            {
                var target = Path.Combine(directory, name[Resources.Length..]);
                var part = target + "." + Guid.NewGuid().ToString("N") + ".part";

                using (var from = assembly.GetManifestResourceStream(name)!)
                using (var to = File.Create(part))
                {
                    from.CopyTo(to);
                }

                File.Move(part, target, overwrite: true);
            }

            File.WriteAllText(marker, stamp);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be written: drawn from what is there, if it
            // is the built-in set at all.
            return File.Exists(marker);
        }
    }

    // Which art the build carries, from every file's name and bytes.
    private static string ArtStamp(System.Reflection.Assembly assembly, IReadOnlyList<string> names)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);

        foreach (var name in names)
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(name + "\n"));

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var copy = new MemoryStream();

            stream.CopyTo(copy);
            hash.AppendData(copy.ToArray());
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..16];
    }

    /// <summary>Where sets live: one directory per set.</summary>
    public static string Root(IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return Path.Combine(paths.Paths.State, "teams", "office");
    }

    /// <summary>
    /// Where the sets live, and which one of them was asked for - or nothing,
    /// where what was asked for is not there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A configured name that no directory answers to comes back as the built-in
    /// set, rather than as one that serves 404s for every piece: the Tech office
    /// is what an unconfigured Loadout looks like, and it should also be what a
    /// misspelt one looks like, rather than something subtly broken. Where the
    /// built-in set cannot be unpacked, no set, and the building's own shapes.
    /// </para>
    /// <para>
    /// The root comes back either way, and that is the point. It used to be
    /// null whenever the set was empty, and both callers handed it straight to
    /// the dashboard as "where the art is" - so a machine with seven offices
    /// installed and no <c>team-office-set</c> configured served no offices at
    /// all. Not the configured one: any of them. The page could not even list
    /// the sets to offer a choice, because the list was behind the same null.
    /// Which set is the default has nothing to do with where the sets live.
    /// </para>
    /// </remarks>
    public static (string Root, string Set) Chosen(IPlatformPaths paths, string? set)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var root = Root(paths);
        var fallback = Unpack(root) ? BuiltIn : string.Empty;

        if (set is not { Length: > 0 } wanted || !Names(wanted))
        {
            return (root, fallback);
        }

        return Directory.Exists(Path.Combine(root, wanted))
            ? (root, wanted)
            : (root, fallback);
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

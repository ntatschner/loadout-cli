using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams;

/// <summary>
/// What a set says about its own room: how big the scene is, and where
/// somebody stands in it.
/// </summary>
/// <param name="Width">The scene's width in pixels, so the page can keep its shape.</param>
/// <param name="Height">The scene's height in pixels.</param>
/// <param name="Desks">
/// Where a person stands, as percentages across and down the scene, the lead's
/// place first. Percentages rather than pixels because the page draws the room
/// at whatever width it has.
/// </param>
/// <param name="Person">
/// How tall a person is in this room, as a percentage of the scene's height.
/// <para>
/// Per room rather than one number everywhere, because the packs do not draw
/// to one scale: a person is a tenth of the open-plan office and under a
/// twelfth of the network floor. A size picked once and applied to all of them
/// is right in one room and floating over the furniture in the others.
/// </para>
/// </param>
public sealed record OfficeRoom(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("desks")] IReadOnlyList<IReadOnlyList<double>> Desks,
    [property: JsonPropertyName("person")] double Person = 10);

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

    /// <summary>
    /// What one set says about its room, or null when it says nothing.
    /// </summary>
    /// <remarks>
    /// The room describes itself, in a file beside its picture, so the page
    /// needs to know nothing about which office it is drawing and a set added
    /// later needs no change here. A set with no room.json draws its people in
    /// a row, which is what every set did before this existed.
    /// </remarks>
    public static OfficeRoom? Room(string root, string set)
    {
        if (!Names(set))
        {
            return null;
        }

        var path = Path.Combine(root, set, "room.json");

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var read = JsonSerializer.Deserialize<OfficeRoom>(File.ReadAllText(path));

            // A room with no scale cannot be drawn to shape, and one with no
            // desks is the same as having no file at all.
            if (read is not { Width: > 0, Height: > 0 })
            {
                return null;
            }

            // How tall a person is has to be a size. Zero or less draws
            // nobody, and a figure taller than a third of the room is not a
            // person in it - both are somebody's typing mistake in a file they
            // edited by hand, and the default is a better answer than a room
            // full of invisible people.
            return read.Person is > 0 and <= 33 ? read : read with { Person = 10 };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Somebody's own directory, edited by hand. A file that will not
            // parse draws the row of people it drew before rather than taking
            // the view out.
            return null;
        }
    }

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

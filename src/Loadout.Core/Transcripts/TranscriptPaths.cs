using System.Text.Json;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Transcripts;

/// <summary>
/// Reads a dotted path out of one line of a transcript.
/// </summary>
/// <remarks>
/// <para>
/// The whole of the description language, shared by the reader that lists
/// sessions and the one that counts tokens so the two cannot disagree about what
/// <c>message.usage.input_tokens</c> means.
/// </para>
/// <para>
/// A path walks objects by name and stops. There is no indexing, no wildcard and
/// no alternative, because every transcript format seen so far puts what is
/// wanted at a fixed place, and a query language nobody asked for is one that has
/// to be documented, tested and kept working forever.
/// </para>
/// </remarks>
internal static class TranscriptPaths
{
    /// <summary>The element at the end of a dotted path, or null if it is not there.</summary>
    private static JsonElement? Element(JsonElement root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var element = root;

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(segment, out element))
            {
                return null;
            }
        }

        return element;
    }

    /// <summary>
    /// The string at the end of a path.
    /// </summary>
    /// <remarks>
    /// Only strings. A value that arrived as a number would have to be rendered
    /// to be used as a name, and how to render somebody else's number is a
    /// decision nobody asked this to make.
    /// </remarks>
    public static string? String(JsonElement root, string? path) =>
        Element(root, path) is { ValueKind: JsonValueKind.String } found
        && found.GetString() is { Length: > 0 } value
            ? value
            : null;

    /// <summary>
    /// The number at the end of a path, and whether anything was there.
    /// </summary>
    /// <param name="root">The line.</param>
    /// <param name="path">Where to look, or null when the format does not record this.</param>
    /// <param name="found">
    /// Set when a number was actually present. The difference between a record
    /// that counted zero and a line that is not an accounting record at all, and
    /// nothing else can tell them apart.
    /// </param>
    public static long Number(JsonElement root, string? path, ref bool found)
    {
        if (Element(root, path) is not { ValueKind: JsonValueKind.Number } element
            || !element.TryGetInt64(out var value))
        {
            return 0;
        }

        found = true;

        return value;
    }

    /// <summary>
    /// A configured path with the home directory filled in.
    /// </summary>
    /// <remarks>
    /// Through the environment provider rather than the real one, so a test can
    /// point a described agent at a temporary tree and so the platform seam
    /// holds: core code carries no literal home path.
    /// </remarks>
    public static string Expand(string path, IEnvironmentProvider environment)
    {
        if (path.StartsWith("~/", StringComparison.Ordinal)
            || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(environment.HomeDirectory, path[2..]);
        }

        return path
            .Replace("${HOME}", environment.HomeDirectory, StringComparison.Ordinal)
            .Replace("$HOME", environment.HomeDirectory, StringComparison.Ordinal);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Loadout.Models.Instructions;

namespace Loadout.Core.Instructions;

/// <summary>
/// A copy that has fallen behind the built-in it replaces.
/// </summary>
/// <param name="Id">The specialist both files claim to be.</param>
/// <param name="Path">Where the copy lives.</param>
/// <param name="Origin">Whether the copy is the workspace's or one project's.</param>
public sealed record StaleCopy(string Id, string Path, SpecialistOrigin Origin);

/// <summary>
/// Tells a copied specialist whether the original has moved on since.
/// </summary>
/// <remarks>
/// <para>
/// Copying a built-in into the workspace makes it editable and reviewable, and
/// costs something: the copy replaces the built-in for good, so an improvement
/// to the original is silently ignored by whoever holds the copy. Nothing in
/// the file would say so.
/// </para>
/// <para>
/// Difference alone cannot be the signal — a copy that differs is a copy doing
/// its job. What matters is whether the <em>built-in</em> changed after the copy
/// was taken, which is only answerable if the copy recorded what the built-in
/// looked like at the time. That is what the fingerprint is for.
/// </para>
/// <para>
/// A copy without one is not reported. Somebody may have written the file by
/// hand rather than exporting it, in which case there is no original it is
/// falling behind, and guessing would put a warning on a specialist that is
/// exactly as its author intended.
/// </para>
/// </remarks>
public static class SpecialistOrigins
{
    /// <summary>What the recorded fingerprint line begins with.</summary>
    public const string Marker = "built-in-fingerprint: ";

    /// <summary>
    /// A short, stable fingerprint of a specialist's text.
    /// </summary>
    /// <remarks>
    /// Line endings are normalised first. A copy taken on Windows and compared
    /// on Linux is the same specialist, and a fingerprint that said otherwise
    /// would report every copy as stale on the other platform.
    /// </remarks>
    public static string Fingerprint(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));

        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>
    /// The copies whose built-in has changed since they were taken.
    /// </summary>
    /// <param name="catalogue">The loaded library, copies and all.</param>
    /// <param name="builtInText">Reads the current text of a built-in by id.</param>
    public static IReadOnlyList<StaleCopy> Stale(
        SpecialistCatalogue catalogue,
        Func<string, string?> builtInText)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(builtInText);

        var stale = new List<StaleCopy>();

        foreach (var specialist in catalogue.Specialists.Values)
        {
            if (specialist.Origin == SpecialistOrigin.BuiltIn
                || specialist.Path.Length == 0)
            {
                continue;
            }

            if (builtInText(specialist.Id) is not { } current)
            {
                // A copy of nothing is somebody's own specialist, not a stale
                // one.
                continue;
            }

            string copy;

            try
            {
                copy = File.ReadAllText(specialist.Path);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (Recorded(copy) is not { } recorded)
            {
                continue;
            }

            if (!string.Equals(recorded, Fingerprint(current), StringComparison.Ordinal))
            {
                stale.Add(new StaleCopy(specialist.Id, specialist.Path, specialist.Origin));
            }
        }

        return stale;
    }

    /// <summary>The fingerprint a copy recorded, or null when it records none.</summary>
    private static string? Recorded(string text)
    {
        var at = text.IndexOf(Marker, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var start = at + Marker.Length;
        var end = start;

        while (end < text.Length && char.IsAsciiLetterOrDigit(text[end]))
        {
            end++;
        }

        return end > start
            ? text[start..end].ToLowerInvariant()
            : null;
    }

    /// <summary>How the finding reads, in one line.</summary>
    public static string Describe(StaleCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} was copied from a built-in that has changed since. Yours is still the one in "
                + "use. Keep it, or take the new one with: loadout instructions export {0} --force",
            copy.Id);
    }
}

using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Common;

/// <summary>
/// Reads the real process environment. The only implementation that touches
/// System.Environment, so tests can substitute a fake everywhere else.
/// </summary>
public sealed class SystemEnvironmentProvider : IEnvironmentProvider
{
    /// <inheritdoc />
    public string? GetVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <inheritdoc />
    public string HomeDirectory
    {
        get
        {
            // The environment is asked first, and in the order this platform
            // actually uses.
            //
            // SpecialFolder.UserProfile reads the account's own record of where
            // its profile is — on Windows from the user token, not from the
            // environment — so it ignores USERPROFILE even when something has
            // deliberately set it. Preferring it meant the home directory could
            // not be pointed anywhere else by anyone: not by a test isolating a
            // run, and not by a person redirecting it on purpose.
            //
            // USERPROFILE comes first on Windows and HOME first elsewhere,
            // which is not interchangeable. A Git Bash or MSYS shell on Windows
            // exports HOME as /c/Users/name — a POSIX spelling that is not a
            // usable Windows path — while USERPROFILE stays correct. Taking
            // HOME there produced paths nothing could open, from a shell plenty
            // of people work in.
            //
            // Each candidate is checked for being a rooted path that exists, so
            // a variable holding something unusable falls through instead of
            // poisoning every path built from it.
            var candidates = OperatingSystem.IsWindows()
                ? new[] { GetVariable("USERPROFILE"), GetVariable("HOME") }
                : [GetVariable("HOME"), GetVariable("USERPROFILE")];

            foreach (var candidate in candidates)
            {
                if (IsUsable(candidate))
                {
                    return candidate!;
                }
            }

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return string.IsNullOrWhiteSpace(profile)
                ? throw new InvalidOperationException(
                    "No home directory could be determined from HOME, USERPROFILE or UserProfile.")
                : profile;
        }
    }

    /// <summary>
    /// Whether a candidate home directory can actually be used.
    /// </summary>
    /// <remarks>
    /// Rooted and present. A relative path, or one naming somewhere that does
    /// not exist, is worse than no answer: every path built from it would be
    /// wrong in a way that surfaces far from here.
    /// </remarks>
    private static bool IsUsable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Path.IsPathRooted(path) && Directory.Exists(path);
        }
        catch (ArgumentException)
        {
            // Illegal characters for this platform, which is exactly the
            // MSYS-style spelling this guard exists for.
            return false;
        }
    }

    /// <inheritdoc />
    public string MachineName => Environment.MachineName;

    /// <inheritdoc />
    public IReadOnlyList<string> PathDirectories =>
        (GetVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // PATH entries on Windows are frequently quoted; an unstripped quote
            // turns into an invalid path character and breaks resolution.
            .Select(p => p.Trim('"'))
            .Where(p => p.Length > 0)
            .ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> ExecutableExtensions => OperatingSystem.IsWindows()
        ? [".exe", ".cmd", ".bat", ".com", ".ps1"]
        : [string.Empty];
}

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
            // The environment is asked first, and that order matters.
            //
            // SpecialFolder.UserProfile reads the account's own record of where
            // its profile is — on Windows from the user token, not from the
            // environment — so it ignores USERPROFILE even when something has
            // deliberately set it. Preferring it meant the home directory could
            // not be pointed anywhere else by anyone: not by a test isolating a
            // run, and not by a person redirecting it on purpose. A subprocess
            // given a throwaway USERPROFILE still read the real one, and found
            // the real agent sessions in it.
            //
            // HOME on Unix and USERPROFILE on Windows are the conventional
            // answers to this question and are set on any ordinary machine.
            // SpecialFolder stays as the fallback for the case that motivated
            // it, which is neither being set at all.
            var home = GetVariable("HOME") ?? GetVariable("USERPROFILE");

            if (!string.IsNullOrWhiteSpace(home))
            {
                return home;
            }

            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return string.IsNullOrWhiteSpace(home)
                ? throw new InvalidOperationException(
                    "No home directory could be determined from HOME, USERPROFILE or UserProfile.")
                : home;
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

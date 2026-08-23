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
            // SpecialFolder.UserProfile is correct on all three platforms and
            // survives the case where HOME or USERPROFILE is unset.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                return home;
            }

            return GetVariable("HOME") ?? GetVariable("USERPROFILE")
                ?? throw new InvalidOperationException(
                    "No home directory could be determined from UserProfile, HOME or USERPROFILE.");
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

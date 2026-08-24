using System.Globalization;
using System.Text;
using Loadout.Core.Git;
using Loadout.Models.Configuration;

namespace Loadout.Core.Statusline;

/// <summary>Everything a status line is drawn from, gathered before rendering.</summary>
/// <param name="Payload">What Claude sent, or null when it sent nothing readable.</param>
/// <param name="ProjectSlug">Registered slug for the repository, or null when it is not one of ours.</param>
/// <param name="ProjectRoot">Repository root, used to shorten the directory.</param>
/// <param name="Git">Branch and cleanliness, or null when git could not be asked.</param>
public sealed record StatuslineInputs(
    StatuslinePayload? Payload,
    string? ProjectSlug,
    string? ProjectRoot,
    GitRepositoryState? Git);

/// <summary>
/// Turns what the launcher knows into the one line Claude prints at the bottom
/// of the screen.
/// <para>
/// Kept free of I/O so it can be tested for what it actually has to get right:
/// that a missing piece removes a segment rather than the line, and that
/// nothing it emits contains a newline. Claude renders the output verbatim, so
/// a stray line break there pushes the conversation around.
/// </para>
/// </summary>
public static class StatuslineRenderer
{
    /// <summary>Above this share of the context window the figure turns red.</summary>
    private const double CrowdedContext = 0.85;

    /// <summary>Above this it turns amber: worth noticing, not yet worth acting on.</summary>
    private const double FillingContext = 0.6;

    public static string Render(StatuslineInputs inputs, StatuslineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(settings);

        var segments = new List<string>();

        if (settings.ShowProject && inputs.ProjectSlug is { Length: > 0 } slug)
        {
            segments.Add(Colour(slug, Cyan, settings));
        }

        if (settings.ShowDirectory)
        {
            var directory = Directory(inputs);

            if (directory is { Length: > 0 })
            {
                segments.Add(Colour(directory, Blue, settings));
            }
        }

        if (settings.ShowGit)
        {
            var git = GitSegment(inputs, settings);

            if (git is { Length: > 0 })
            {
                segments.Add(git);
            }
        }

        if (settings.ShowModel && inputs.Payload?.Model?.DisplayName is { Length: > 0 } model)
        {
            segments.Add(Colour(model, Dim, settings));
        }

        if (settings.ShowContext && inputs.Payload?.ContextWindow?.UsedFraction is { } used)
        {
            var percentage = (int)Math.Round(used * 100, MidpointRounding.AwayFromZero);

            var colour = used >= CrowdedContext ? Red
                : used >= FillingContext ? Yellow
                : Dim;

            segments.Add(Colour(
                percentage.ToString(CultureInfo.InvariantCulture) + "% ctx",
                colour,
                settings));
        }

        var separator = string.IsNullOrEmpty(settings.Separator) ? " | " : settings.Separator;

        return Flatten(string.Join(Colour(separator, Dim, settings), segments));
    }

    /// <summary>
    /// Where the session is, said as briefly as it can be without becoming
    /// ambiguous: relative to the repository root when inside one, the name of
    /// the repository when at its root, and a home-shortened path when outside
    /// any repository at all.
    /// </summary>
    private static string? Directory(StatuslineInputs inputs)
    {
        var current = inputs.Payload?.Workspace?.CurrentDir ?? inputs.Payload?.Cwd;

        if (string.IsNullOrWhiteSpace(current))
        {
            return null;
        }

        var root = inputs.ProjectRoot ?? inputs.Git?.Root;

        if (root is { Length: > 0 })
        {
            var relative = Relative(root, current);

            if (relative is not null)
            {
                // At the root itself the relative path is ".", which tells
                // nobody anything. The name of the folder does.
                return relative == "."
                    ? Path.GetFileName(root.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar))
                    : relative;
            }
        }

        return Shorten(current);
    }

    /// <summary>
    /// The relative path from root to path, or null when path is not under root.
    /// <para>
    /// Compared case-insensitively only on Windows. Doing it everywhere would
    /// be wrong on the case-sensitive filesystems this tool has to support, and
    /// this is a display path — getting it wrong shows the long form, which is
    /// merely ugly rather than incorrect.
    /// </para>
    /// </summary>
    private static string? Relative(string root, string path)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(path);

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!fullPath.StartsWith(fullRoot, comparison))
            {
                return null;
            }

            var relative = Path.GetRelativePath(fullRoot, fullPath);

            return relative.StartsWith("..", StringComparison.Ordinal)
                ? null
                : relative.Replace('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Replaces the home directory with a tilde, the way a shell prompt does.</summary>
    private static string Shorten(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (home is { Length: > 0 } && path.StartsWith(home, StringComparison.Ordinal))
        {
            return "~" + path[home.Length..].Replace('\\', '/');
        }

        return path.Replace('\\', '/');
    }

    /// <summary>
    /// Branch, an asterisk when the tree is dirty, and the worktree name when
    /// the session is in a linked one — which is the case where somebody most
    /// needs telling, because two worktrees look identical otherwise.
    /// </summary>
    private static string? GitSegment(StatuslineInputs inputs, StatuslineSettings settings)
    {
        var branch = inputs.Git?.Branch;
        var worktree = inputs.Payload?.Workspace?.GitWorktree;

        if (string.IsNullOrWhiteSpace(branch))
        {
            return string.IsNullOrWhiteSpace(worktree)
                ? null
                : Colour(worktree, Magenta, settings);
        }

        var dirty = inputs.Git?.IsClean == false;
        var text = dirty ? branch + "*" : branch;

        var rendered = Colour(text, dirty ? Yellow : Green, settings);

        return string.IsNullOrWhiteSpace(worktree)
            ? rendered
            : rendered + Colour(" (" + worktree + ")", Magenta, settings);
    }

    private const string Reset = "\u001b[0m";
    private const string Dim = "\u001b[2m";
    private const string Red = "\u001b[31m";
    private const string Green = "\u001b[32m";
    private const string Yellow = "\u001b[33m";
    private const string Blue = "\u001b[34m";
    private const string Magenta = "\u001b[35m";
    private const string Cyan = "\u001b[36m";

    private static string Colour(string text, string code, StatuslineSettings settings) =>
        settings.Colour ? code + text + Reset : text;

    /// <summary>
    /// Removes newlines and control characters. Claude prints this line as it
    /// stands, so anything that moves the cursor damages the display around it.
    /// </summary>
    private static string Flatten(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            if (c is '\r' or '\n')
            {
                continue;
            }

            // The escape character is the point of the colour codes, so it
            // stays; every other control character goes.
            if (!char.IsControl(c) || c == '\u001b')
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Trim();
    }
}

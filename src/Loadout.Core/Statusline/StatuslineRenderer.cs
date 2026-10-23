using System.Globalization;
using System.Text;
using Loadout.Core.Git;
using Loadout.Core.Sessions.Hosting;
using Loadout.Models.Configuration;

namespace Loadout.Core.Statusline;

/// <summary>Everything a status line is drawn from, gathered before rendering.</summary>
/// <param name="Payload">What Claude sent, or null when it sent nothing readable.</param>
/// <param name="ProjectSlug">Registered slug for the repository, or null when it is not one of ours.</param>
/// <param name="ProjectRoot">Repository root, used to shorten the directory.</param>
/// <param name="Git">Branch and cleanliness, or null when git could not be asked.</param>
/// <param name="Spend">
/// The last answer the spending thresholds gave, read from disk rather than
/// worked out here. Null when nothing has been computed, which is the state
/// until somebody sets a threshold.
/// </param>
/// <param name="Specialists">
/// What the last launch composed, read from disk. Null when nothing has
/// launched, which is the state in a session started outside the launcher.
/// </param>
/// <param name="Now">
/// The moment the line is drawn for. Null leaves out every time segment,
/// which keeps the renderer free of a clock of its own.
/// </param>
/// <param name="Zone">The zone times are shown in; null means this machine's.</param>
/// <param name="Times">What the session's times are, or null when they could not be read.</param>
/// <param name="FixedTimes">
/// Show only values that do not move between redraws: no clock, no ages.
/// Set under the accessibility profile's <c>redraw: never</c>, because a
/// screen reader announces a line that changes on its own.
/// </param>
/// <param name="Width">
/// Columns the line has to fit in, or null when nobody said. Only time
/// segments are dropped to fit.
/// </param>
public sealed record StatuslineInputs(
    StatuslinePayload? Payload,
    string? ProjectSlug,
    string? ProjectRoot,
    GitRepositoryState? Git,
    Usage.SpendNotice? Spend = null,
    LoadedSpecialists? Specialists = null,
    DateTimeOffset? Now = null,
    TimeZoneInfo? Zone = null,
    StatuslineTimes? Times = null,
    bool FixedTimes = false,
    int? Width = null);

/// <summary>
/// The session's times, already read from wherever they live. Plain values,
/// so the renderer depends on no reader.
/// </summary>
/// <param name="TurnStartedAt">When the current or last turn began.</param>
/// <param name="TurnEndedAt">When the last turn finished; null while one is running.</param>
/// <param name="PendingToolSince">When a tool call still without a result was made.</param>
/// <param name="PermissionAsked">Whether that call is waiting on a permission prompt.</param>
/// <param name="PermissionAt">When the permission prompt was raised.</param>
/// <param name="BackgroundOutstanding">Whether background work is still running.</param>
/// <param name="WakeupDue">When a scheduled wake-up is due.</param>
/// <param name="LastWriteAt">When the agent last changed a file.</param>
/// <param name="SessionLength">How long the session has run.</param>
/// <param name="LastCommitAt">When the branch was last committed to.</param>
public sealed record StatuslineTimes(
    DateTimeOffset? TurnStartedAt = null,
    DateTimeOffset? TurnEndedAt = null,
    DateTimeOffset? PendingToolSince = null,
    bool PermissionAsked = false,
    DateTimeOffset? PermissionAt = null,
    bool BackgroundOutstanding = false,
    DateTimeOffset? WakeupDue = null,
    DateTimeOffset? LastWriteAt = null,
    TimeSpan? SessionLength = null,
    DateTimeOffset? LastCommitAt = null);

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
        var times = new List<(TimeSegment Kind, string Text)>();

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

        if (settings.ShowSpecialists && inputs.Specialists is { Ids.Count: > 0 } loaded)
        {
            // A count, not a list. Seventy-odd ids will not fit on a line
            // somebody is trying to read past, and the number is the part that
            // changes: it says the composition is what you think it is.
            segments.Add(Colour(
                loaded.Mode is { Length: > 0 } mode
                    ? $"{loaded.Ids.Count} spec/{mode}"
                    : $"{loaded.Ids.Count} spec",
                Dim,
                settings));
        }

        // After where the session is and before spend: these say what it is
        // doing, which is about this session, and spend stays last.
        if (inputs.Now is { } now && inputs.Times is { } sessionTimes)
        {
            times.AddRange(TimeSegments(
                sessionTimes,
                now,
                inputs.Zone ?? TimeZoneInfo.Local,
                inputs.FixedTimes,
                settings));
        }

        string? spend = null;

        // Last, because it is the only segment that is not about this session.
        // Everything to its left describes where you are; this describes what
        // has been spent, and it is the thing to glance at rather than read.
        if (settings.ShowSpend && inputs.Spend is { Lines.Count: > 0 })
        {
            spend = Colour($"spend! ({inputs.Spend.Lines.Count})", Yellow, settings);
        }

        var separator = Colour(
            string.IsNullOrEmpty(settings.Separator) ? " | " : settings.Separator,
            Dim,
            settings);

        string Join() => Flatten(string.Join(
            separator,
            segments.Concat(times.Select(t => t.Text)).Concat(spend is null ? [] : [spend])));

        var line = Join();

        if (inputs.Width is not { } width)
        {
            return line;
        }

        // Only time segments give way, in an order that keeps the ones that
        // need acting on: the clock is on the terminal's own screen anyway,
        // and waiting is the reason the group exists. Spend is never dropped,
        // because a crossed threshold missed is money.
        foreach (var kind in DropOrder)
        {
            if (VisibleLength(line) <= width)
            {
                break;
            }

            if (times.RemoveAll(t => t.Kind == kind) > 0)
            {
                line = Join();
            }
        }

        return line;
    }

    private enum TimeSegment
    {
        Waiting,
        Write,
        Clock,
        Session,
        Turn,
        Commit,
    }

    private static readonly TimeSegment[] DropOrder =
    [
        TimeSegment.Clock,
        TimeSegment.Session,
        TimeSegment.Turn,
        TimeSegment.Commit,
        TimeSegment.Write,
        TimeSegment.Waiting,
    ];

    /// <summary>
    /// The time group, in the order it is drawn: what the session is waiting
    /// on, the last write, the clock, the session, the last turn, the last
    /// commit. A segment whose time is not known is left out.
    /// </summary>
    private static IEnumerable<(TimeSegment, string)> TimeSegments(
        StatuslineTimes times,
        DateTimeOffset now,
        TimeZoneInfo zone,
        bool fixedTimes,
        StatuslineSettings settings)
    {
        var hour24 = settings.Time.Hour24;

        // An age moves on every redraw, so under redraw-never it is a clock
        // time instead, whatever the setting says.
        var absolute = settings.Time.Absolute || fixedTimes;

        string At(DateTimeOffset at) => TimeFormat.Absolute(at, now, zone, hour24);
        string When(DateTimeOffset at) => absolute ? At(at) : TimeFormat.Relative(at, now);

        if (settings.ShowWaiting && Waiting(times, now, settings, At) is { } waiting)
        {
            yield return (TimeSegment.Waiting, waiting);
        }

        if (settings.ShowLastWrite && times.LastWriteAt is { } write)
        {
            yield return (TimeSegment.Write, Aged(
                "write " + When(write), now - write, settings.LastWriteAfter, settings));
        }

        if (settings.ShowClock && !fixedTimes)
        {
            yield return (TimeSegment.Clock, Colour(TimeFormat.Clock(now, zone, hour24), Dim, settings));
        }

        if (settings.ShowSession && times.SessionLength is { } length)
        {
            yield return (TimeSegment.Session, Colour(
                absolute ? "started " + At(now - length) : "session " + TimeFormat.Duration(length),
                Dim,
                settings));
        }

        // Completed turns only: a duration of a finished turn cannot go
        // stale, where one still running would need a redraw to stay true.
        if (settings.ShowTurn
            && times is { TurnStartedAt: { } started, TurnEndedAt: { } ended }
            && ended >= started)
        {
            yield return (TimeSegment.Turn, Colour("turn " + TimeFormat.Duration(ended - started), Dim, settings));
        }

        if (settings.ShowLastCommit && times.LastCommitAt is { } commit)
        {
            yield return (TimeSegment.Commit, Aged(
                "commit " + When(commit), now - commit, settings.LastCommitAfter, settings));
        }
    }

    /// <summary>
    /// What the session is waiting on, and since when. Null when nothing is
    /// known about the turn at all.
    /// </summary>
    private static string? Waiting(
        StatuslineTimes times,
        DateTimeOffset now,
        StatuslineSettings settings,
        Func<DateTimeOffset, string> at)
    {
        string Since(string what, DateTimeOffset since) =>
            Aged($"{what} since {at(since)}", now - since, settings.WaitingAfter, settings);

        if (times.TurnEndedAt is not { } ended)
        {
            if (times.PendingToolSince is { } tool)
            {
                return times.PermissionAsked
                    ? Since("permission", times.PermissionAt ?? tool)
                    : Since("on a tool", tool);
            }

            return times.TurnStartedAt is { } started ? Since("working", started) : null;
        }

        if (times.WakeupDue is { } due)
        {
            // The same instant the dashboard calls a wake-up late, from the
            // same constant, so the two never disagree about a session.
            return now >= due + SessionSignals.WakeupGrace
                ? Colour("due " + at(due), Red, settings)
                : Colour("wakes " + at(due), Dim, settings);
        }

        return times.BackgroundOutstanding
            ? Since("background", ended)
            : Since("waiting for you", ended);
    }

    /// <summary>Dim, then amber, then red as the age passes the setting's thresholds.</summary>
    private static string Aged(string text, TimeSpan age, string? thresholds, StatuslineSettings settings)
    {
        var (amber, red) = TimeFormat.Thresholds(thresholds);

        var colour = red is { } r && age >= r ? Red
            : amber is { } a && age >= a ? Yellow
            : Dim;

        return Colour(text, colour, settings);
    }

    /// <summary>The length a person sees: escape sequences take no columns.</summary>
    private static int VisibleLength(string line)
    {
        var length = 0;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\u001b' && i + 1 < line.Length && line[i + 1] == '[')
            {
                i += 2;

                while (i < line.Length && !char.IsAsciiLetter(line[i]))
                {
                    i++;
                }

                continue;
            }

            length++;
        }

        return length;
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

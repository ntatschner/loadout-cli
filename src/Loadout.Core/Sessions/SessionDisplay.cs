using System.Globalization;

namespace Loadout.Core.Sessions;

/// <summary>
/// Renders a session the same way everywhere it is shown.
/// <para>
/// Lives here rather than in either front end because the command line and the
/// interactive launcher both list sessions, and a list that reads differently
/// in the two places is a list somebody has to learn twice.
/// </para>
/// </summary>
public static class SessionDisplay
{
    private const int WhenWidth = 14;
    private const int AgentWidth = 6;
    private const int ProjectWidth = 18;

    /// <summary>
    /// The four columns of a session line, each padded or cut to a fixed width.
    /// <para>
    /// Fixed widths rather than a table because a table reflows its columns to
    /// fit: one long title turns every other row into three wrapped ones, and a
    /// list that cannot be scanned by eye is not worth printing.
    /// </para>
    /// </summary>
    public static (string When, string Agent, string Project, string What) Columns(
        AgentSession session,
        int width)
    {
        ArgumentNullException.ThrowIfNull(session);

        var project = session.ProjectSlug is { Length: > 0 } slug ? slug : "-";

        // Whatever is left after the fixed columns and the spaces between them.
        var remaining = Math.Max(12, width - WhenWidth - AgentWidth - ProjectWidth - 4);

        return (
            Fit(Ago(session.LastActive), WhenWidth),
            Fit(session.Agent, AgentWidth),
            Fit(project, ProjectWidth),
            Fit(session.Label, remaining));
    }

    /// <summary>One padded line, for a menu that draws its own selection marker.</summary>
    public static string Line(AgentSession session, int width)
    {
        var (when, agent, project, what) = Columns(session, width);

        return $"{when} {agent} {project} {what}".TrimEnd();
    }

    /// <summary>
    /// How long ago, in the roughest useful unit. An exact timestamp is worse
    /// here: the question being answered is "was this the one from this
    /// morning", not "when precisely did it end".
    /// </summary>
    public static string Ago(DateTimeOffset when)
    {
        var elapsed = DateTimeOffset.UtcNow - when;

        // A file timestamp ahead of the clock — a copied tree, a machine whose
        // time was corrected — is not worth reporting as "in three hours".
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return Plural((int)elapsed.TotalMinutes, "minute");
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return Plural((int)elapsed.TotalHours, "hour");
        }

        if (elapsed < TimeSpan.FromDays(30))
        {
            return Plural((int)elapsed.TotalDays, "day");
        }

        return when.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }

    private static string Plural(int count, string unit) =>
        count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";

    /// <summary>Pads to a width, or truncates with an ellipsis when too long.</summary>
    private static string Fit(string text, int width)
    {
        if (text.Length <= width)
        {
            return text.PadRight(width);
        }

        return width <= 1 ? text[..width] : text[..(width - 1)] + "…";
    }
}

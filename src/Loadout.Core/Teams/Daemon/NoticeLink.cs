namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// Where a notice about a run sends whoever reads it.
/// </summary>
/// <remarks>
/// <para>
/// A notice goes through somebody else's service - Slack, Discord, Teams,
/// Telegram - and stays in that service's history for as long as it keeps
/// history. So the link never carries the dashboard's token, which is the
/// whole key to the dashboard: anything after a '?' in either address is
/// dropped, and with it a token somebody pasted into the link address by
/// mistake. Whoever follows the link signs in there as they would anywhere.
/// </para>
/// <para>
/// The link address is for reading a notice somewhere other than this
/// machine: a tailnet name or a reverse proxy in front of the dashboard. A
/// dashboard listening on 127.0.0.1 is a dead link on a phone, which is why
/// it is a setting rather than worked out from where the dashboard listens.
/// </para>
/// </remarks>
public static class NoticeLink
{
    /// <summary>The link for one run.</summary>
    /// <param name="linkAddress">The machine's <c>team-link-address</c>, or null or empty where it is unset.</param>
    /// <param name="dashboard">Where the dashboard is serving, as it printed it, or null where it is not.</param>
    /// <param name="run">The run the notice is about.</param>
    /// <returns>
    /// Studio's page for the run, or the command that shows it where there is
    /// no address to link to.
    /// </returns>
    public static string For(string? linkAddress, string? dashboard, string run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run);

        return To(linkAddress, dashboard, $"teams/{Uri.EscapeDataString(run)}", $"loadout team status {run}");
    }

    /// <summary>The link for one of Studio's pages.</summary>
    /// <param name="linkAddress">The machine's <c>team-link-address</c>, or null or empty where it is unset.</param>
    /// <param name="dashboard">Where the dashboard is serving, as it printed it, or null where it is not.</param>
    /// <param name="route">Studio's route for the page, such as <c>teams</c>, already escaped.</param>
    /// <param name="otherwise">What to say where there is no address to link to: the command that shows the same.</param>
    public static string To(string? linkAddress, string? dashboard, string route, string otherwise)
    {
        var root = Root(linkAddress) ?? Root(dashboard);

        // Studio is served at /studio/ whichever page the address itself
        // opens, so the route works on a machine set to classic too.
        return root is null ? otherwise : $"{root}studio/#{route}";
    }

    /// <summary>Whether a link address can be used, and why not where it cannot.</summary>
    /// <returns>Null where it is fine, or what is wrong with it.</returns>
    public static string? Problem(string? linkAddress)
    {
        if (string.IsNullOrWhiteSpace(linkAddress))
        {
            return null;
        }

        if (!Uri.TryCreate(linkAddress.Trim(), UriKind.Absolute, out var where)
            || where.Scheme is not ("http" or "https"))
        {
            return $"'{linkAddress}' is not an address: give one starting http:// or https://, "
                + "such as https://desk.example.ts.net:4800";
        }

        if (where.Query.Length > 0 || where.Fragment.Length > 0)
        {
            // Said rather than quietly dropped, because the likeliest thing
            // after a '?' here is the dashboard's token, and keeping it would
            // put the key to the dashboard in every chat a notice goes to.
            return "Give the link address without anything after a '?' or '#'. A notice never "
                + "carries the dashboard's token; whoever follows the link signs in there.";
        }

        return null;
    }

    /// <summary>The address up to and including its path, ending in '/', or null.</summary>
    private static string? Root(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)
            || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var where)
            || where.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var path = where.AbsolutePath.EndsWith('/') ? where.AbsolutePath : where.AbsolutePath + "/";

        return $"{where.Scheme}://{where.Authority}{path}";
    }
}

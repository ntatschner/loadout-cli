using System.Net;
using System.Text.Json;
using Loadout.Core.Events;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// The event history under <c>/api/v1/eventlog</c>: what needed you, and what you did.
/// </summary>
/// <remarks>
/// <para>
/// <list type="table">
/// <item><term>GET /api/v1/eventlog</term><description>Events newest first, filtered by the query, which is <see cref="EventFilter"/>'s canonical string plus <c>limit</c>: <c>{ items, matched, unread, filter, days }</c>, or a 400 naming what it cannot read.</description></item>
/// <item><term>GET /api/v1/eventlog/unread</term><description>The count the rail shows: <c>{ unread }</c>.</description></item>
/// <item><term>POST /api/v1/eventlog/read</term><description>Marks <c>{ ids }</c> read, or everything up to now for <c>{ all: true }</c>: <c>{ unread }</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// Not <c>/api/v1/events</c>, which is the change stream every page already
/// holds open. That says something changed; this is what changed, kept.
/// </para>
/// <para>
/// Read only, apart from the read marks, on purpose. Answering a question
/// from here would be a second place to answer it, and the run or session
/// it belongs to is where the context is. Each event says where that is.
/// </para>
/// </remarks>
internal static class EventLogApi
{
    /// <summary>Where the history lives.</summary>
    public const string Root = "/api/v1/eventlog";

    private const string WatchOnlyRefusal =
        "This dashboard is only watching, so it cannot mark anything read. Use 'loadout events --mark-read' in a terminal.";

    /// <summary>Answers a request whose path is <see cref="Root"/> or below it.</summary>
    public static async Task HandleAsync(
        HttpListenerContext context,
        EventStore store,
        string path,
        bool watchOnly,
        int days,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var method = context.Request.HttpMethod;
        var parts = path[Root.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);

        switch (parts)
        {
            case [] when method == "GET":
                await ListAsync(context, store, days, now).ConfigureAwait(false);
                return;

            case ["unread"] when method == "GET":
                await JsonAsync(context, 200, new { unread = EventFeed.Query(store, EventFilter.Everything, now, 1).Unread }).ConfigureAwait(false);
                return;

            case ["read"] when method == "POST" && watchOnly:
                await PlainAsync(context, 403, WatchOnlyRefusal).ConfigureAwait(false);
                return;

            case ["read"] when method == "POST":
                await MarkAsync(context, store, now, ct).ConfigureAwait(false);
                return;

            case [] or ["unread"] or ["read"]:
                await PlainAsync(context, 405, "The history is read with GET; events are marked read with POST to /api/v1/eventlog/read.").ConfigureAwait(false);
                return;

            default:
                await PlainAsync(context, 404, "There is nothing here.").ConfigureAwait(false);
                return;
        }
    }

    private static async Task ListAsync(HttpListenerContext context, EventStore store, int days, DateTimeOffset now)
    {
        var query = context.Request.Url?.Query ?? string.Empty;
        var limit = EventFeed.DefaultLimit;
        var filterParts = new List<string>();

        // The limit is the route's own; everything else is the filter's, and
        // the filter says what it cannot read.
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("limit=", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(part["limit=".Length..], out limit) || limit < 1)
                {
                    await PlainAsync(context, 400, $"The limit is a whole number from 1 to {EventFeed.MaximumLimit}.").ConfigureAwait(false);

                    return;
                }
            }
            else
            {
                filterParts.Add(part);
            }
        }

        var filter = EventFilter.Parse(string.Join('&', filterParts));

        if (filter.Failed)
        {
            await PlainAsync(context, 400, filter.Error!).ConfigureAwait(false);

            return;
        }

        var page = EventFeed.Query(store, filter.Value!, now, limit);

        await JsonAsync(context, 200, new
        {
            items = page.Items,
            matched = page.Matched,
            unread = page.Unread,
            filter = filter.Value!.Format(),
            days,
        }).ConfigureAwait(false);
    }

    private static async Task MarkAsync(HttpListenerContext context, EventStore store, DateTimeOffset now, CancellationToken ct)
    {
        Marking? asked;

        try
        {
            asked = await JsonSerializer.DeserializeAsync<Marking>(context.Request.InputStream, LaunchApi.Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            asked = null;
        }

        if (asked is null || (!asked.All && asked.Ids is not { Count: > 0 }))
        {
            await PlainAsync(context, 400, "Say which events: { \"ids\": [...] }, or { \"all\": true } for everything so far.").ConfigureAwait(false);

            return;
        }

        if (asked.All)
        {
            store.MarkAllRead(now);
        }
        else
        {
            store.MarkRead(asked.Ids!.Take(EventStore.MaximumMarks));
        }

        await JsonAsync(context, 200, new { unread = EventFeed.Query(store, EventFilter.Everything, now, 1).Unread }).ConfigureAwait(false);
    }

    private sealed record Marking(IReadOnlyList<string>? Ids, bool All = false);

    private static Task JsonAsync(HttpListenerContext context, int status, object body) =>
        DashboardServer.WriteAsync(context, status, "application/json; charset=utf-8", JsonSerializer.Serialize(body, LaunchApi.Json));

    private static Task PlainAsync(HttpListenerContext context, int status, string body) =>
        DashboardServer.WriteAsync(context, status, "text/plain; charset=utf-8", body);
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams.Daemon;

/// <summary>A session the running daemon hosts, as its sessions route lists it.</summary>
/// <param name="Id">The daemon's id for it.</param>
/// <param name="Title">What it is called now.</param>
/// <param name="LaunchTitle">What it was launched as.</param>
/// <param name="Ended">Whether it has ended.</param>
/// <param name="Project">The project it runs in, or null for a command.</param>
/// <param name="Agent">The agent it launched, where it named one.</param>
/// <param name="Kind"><c>launch</c> for an agent session, <c>command</c> for a command that asks as it goes.</param>
/// <param name="Needs">What its agent's hooks say it needs, or null.</param>
/// <param name="NamesAgent">Whether its agent is known to be given its name as well.</param>
/// <param name="StartedAt">When it started.</param>
public sealed record HostedSessionRow(
    string Id,
    string Title,
    string LaunchTitle,
    bool Ended,
    string? Project,
    string? Agent,
    string Kind,
    string? Needs,
    bool NamesAgent,
    DateTimeOffset StartedAt);

/// <summary>The sessions the running daemon hosts, read and changed through the routes Studio uses.</summary>
public interface IDaemonSessions
{
    /// <summary>Every session, oldest first.</summary>
    Task<OperationResult<IReadOnlyList<HostedSessionRow>>> ListAsync(CancellationToken ct = default);

    /// <summary>Gives one a new name, or its launch name back for null.</summary>
    Task<OperationResult<HostedSessionRow>> RenameAsync(string id, string? name, CancellationToken ct = default);

    /// <summary>Ends one and leaves it listed, ended.</summary>
    Task<OperationResult<HostedSessionRow>> EndAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// The daemon's hosted sessions from another process, over the daemon's own
/// sessions routes.
/// </summary>
/// <remarks>
/// <para>
/// Through the same routes as Studio, with the same checks, so a terminal and
/// a page cannot disagree about what renaming or ending does. The address and
/// token come from the daemon's note, and only while the daemon that wrote it
/// is still running: an address left by one that was killed is a closed port.
/// </para>
/// <para>
/// A request from here says nothing about where it came from, which the
/// daemon lets through on its token alone, as it does every tool.
/// </para>
/// </remarks>
public sealed class DaemonSessions : IDaemonSessions
{
    /// <summary>What every call says when no daemon is there to ask.</summary>
    public const string NoDaemon = "No daemon is running, so nothing is hosting a session. Start one with: loadout daemon";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly IPlatformPaths _paths;
    private readonly IProcessInspector _processes;

    public DaemonSessions(HttpClient client, IPlatformPaths paths, IProcessInspector processes)
    {
        _client = client;
        _paths = paths;
        _processes = processes;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<HostedSessionRow>>> ListAsync(CancellationToken ct = default)
    {
        var sent = await SendAsync(HttpMethod.Get, string.Empty, null, ct).ConfigureAwait(false);

        return sent.Failed
            ? OperationResult<IReadOnlyList<HostedSessionRow>>.Fail(sent.Error!, sent.ExitCode)
            : OperationResult<IReadOnlyList<HostedSessionRow>>.Ok(
                JsonSerializer.Deserialize<List<Shown>>(sent.Value!, Json)?.Select(Row).ToList() ?? []);
    }

    /// <inheritdoc />
    public Task<OperationResult<HostedSessionRow>> RenameAsync(string id, string? name, CancellationToken ct = default) =>
        OneAsync(HttpMethod.Patch, "/" + Uri.EscapeDataString(id), new { title = name }, ct);

    /// <inheritdoc />
    public Task<OperationResult<HostedSessionRow>> EndAsync(string id, CancellationToken ct = default) =>
        OneAsync(HttpMethod.Post, "/" + Uri.EscapeDataString(id) + "/end", null, ct);

    private async Task<OperationResult<HostedSessionRow>> OneAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var sent = await SendAsync(method, path, body, ct).ConfigureAwait(false);

        return sent.Failed
            ? OperationResult<HostedSessionRow>.Fail(sent.Error!, sent.ExitCode)
            : OperationResult<HostedSessionRow>.Ok(Row(JsonSerializer.Deserialize<Shown>(sent.Value!, Json)!));
    }

    /// <summary>The body of a request that succeeded, or what the daemon said instead.</summary>
    private async Task<OperationResult<string>> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (DaemonNote.Live(_paths, _processes) is not { Address: { Length: > 0 } address }
            || !Uri.TryCreate(address, UriKind.Absolute, out var where))
        {
            return OperationResult<string>.Fail(NoDaemon);
        }

        var token = System.Web.HttpUtility.ParseQueryString(where.Query)["token"];
        using var request = new HttpRequestMessage(method, $"{where.Scheme}://{where.Authority}{SessionsApi.Root}{path}");

        if (token is { Length: > 0 })
        {
            request.Headers.Add("X-Loadout-Token", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using var patience = CancellationTokenSource.CreateLinkedTokenSource(ct);
        patience.CancelAfter(Patience);

        try
        {
            using var response = await _client.SendAsync(request, patience.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(patience.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return OperationResult<string>.Fail(text is { Length: > 0 } ? text : "The daemon has no session with that id.");
            }

            return response.IsSuccessStatusCode
                ? OperationResult<string>.Ok(text)
                : OperationResult<string>.Fail(text is { Length: > 0 } ? text : $"The daemon answered {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return OperationResult<string>.Fail("The daemon did not answer. It may be stopping: loadout daemon status");
        }
    }

    private static HostedSessionRow Row(Shown shown) => new(
        shown.Id,
        shown.Title,
        shown.LaunchTitle ?? shown.Title,
        shown.Ended,
        shown.Project,
        shown.Agent,
        shown.Kind ?? "launch",
        shown.Needs,
        shown.NamesAgent,
        shown.StartedAt);

    private sealed record Shown(
        string Id,
        string Title,
        string? LaunchTitle,
        bool Ended,
        string? Project,
        string? Agent,
        string? Kind,
        string? Needs,
        bool NamesAgent,
        DateTimeOffset StartedAt);
}

/// <summary>Finding a hosted session by what somebody typed for it.</summary>
public static class HostedSessionMatch
{
    /// <summary>
    /// The one session an id, a unique start of an id, or an exact name
    /// (ignoring case) names; or why there is not exactly one, listing the
    /// candidates where there are several.
    /// </summary>
    public static OperationResult<HostedSessionRow> Find(IReadOnlyList<HostedSessionRow> sessions, string asked)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var wanted = asked?.Trim() ?? string.Empty;

        if (wanted.Length == 0)
        {
            return OperationResult<HostedSessionRow>.Fail("Say which session: its id, the start of its id, or its name.");
        }

        if (sessions.FirstOrDefault(one => string.Equals(one.Id, wanted, StringComparison.Ordinal)) is { } exact)
        {
            return OperationResult<HostedSessionRow>.Ok(exact);
        }

        var matches = sessions
            .Where(one => one.Id.StartsWith(wanted, StringComparison.Ordinal)
                || string.Equals(one.Title, wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            1 => OperationResult<HostedSessionRow>.Ok(matches[0]),
            0 => OperationResult<HostedSessionRow>.Fail($"No hosted session is called '{wanted}' or has an id starting with it. See them with: loadout session list"),
            _ => OperationResult<HostedSessionRow>.Fail(
                $"'{wanted}' could be any of {matches.Count} sessions, so nothing was changed. Name one by its id: "
                + string.Join(", ", matches.Select(one => $"{one.Id} ({one.Title})"))),
        };
    }
}

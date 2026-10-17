using System.Net;
using System.Text.Json;
using Loadout.Core.Sessions.Hosting;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// What Studio's New session sheet offers and what a launch would load,
/// supplied by whoever starts the server.
/// </summary>
/// <remarks>
/// An interface here and its implementation beside the commands, because the
/// agents a machine has are known to the agent registry, which Core does not
/// reach. Both answers come from <see cref="Sessions.LaunchSheet"/>, the
/// terminal launcher's sheet's own source, so the two sheets cannot describe
/// different sessions.
/// </remarks>
public interface IStudioLaunch
{
    /// <summary>The projects, and for one of them its agents, profiles, working trees and recorded tasks.</summary>
    Task<object> SheetAsync(string? project, string? agent, CancellationToken ct);

    /// <summary>What a launch as described would load, and the command it is.</summary>
    Task<object> PreviewAsync(HostedLaunch launch, CancellationToken ct);
}

/// <summary>Studio's launch sheet under <c>/api/v1/launch</c>.</summary>
/// <remarks>
/// <list type="table">
/// <item><term>GET /api/v1/launch?project=&amp;agent=</term><description>What the sheet offers.</description></item>
/// <item><term>POST /api/v1/launch/preview</term><description>The same body as starting a session; what it would load and the command it runs.</description></item>
/// </list>
/// Both only read. Starting the session is a POST to <c>/api/v1/sessions</c>.
/// </remarks>
internal static class LaunchApi
{
    public const string Root = "/api/v1/launch";

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task HandleAsync(HttpListenerContext context, IStudioLaunch launch, string path, CancellationToken ct)
    {
        var method = context.Request.HttpMethod;

        if (path == Root && method == "GET")
        {
            var sheet = await launch
                .SheetAsync(context.Request.QueryString["project"], context.Request.QueryString["agent"], ct)
                .ConfigureAwait(false);

            await JsonAsync(context, 200, sheet).ConfigureAwait(false);

            return;
        }

        if (path == Root + "/preview" && method == "POST")
        {
            if (await SessionsApi.ReadLaunchAsync(context, ct).ConfigureAwait(false) is not { } asked)
            {
                await DashboardServer.WriteAsync(context, 400, "text/plain; charset=utf-8",
                    "The body should be JSON naming a 'project'.").ConfigureAwait(false);

                return;
            }

            await JsonAsync(context, 200, await launch.PreviewAsync(asked, ct).ConfigureAwait(false)).ConfigureAwait(false);

            return;
        }

        await DashboardServer.WriteAsync(context, path.StartsWith(Root + "/", StringComparison.Ordinal) || path == Root ? 405 : 404,
            "text/plain; charset=utf-8", "The launch sheet is read with GET, and its preview asked with POST.").ConfigureAwait(false);
    }

    private static Task JsonAsync(HttpListenerContext context, int status, object body) =>
        DashboardServer.WriteAsync(context, status, "application/json; charset=utf-8", JsonSerializer.Serialize(body, Json));
}

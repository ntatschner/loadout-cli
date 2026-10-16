using System.Net;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// Studio's page and its files, served from the bundle built into this
/// assembly under <c>studio/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Served without the token, as the classic page is: these are the same bytes
/// for everybody, and holding nothing secret is what lets an installed app
/// window open before it has signed in and then say how to. Everything the
/// page reads about this machine goes through the API, which does check.
/// </para>
/// <para>
/// A build made without Node has no bundle, and <c>/studio/</c> says so
/// rather than answering a 404 that looks like a mistyped address.
/// </para>
/// </remarks>
internal static class StudioAssets
{
    /// <summary>Where Studio is served from.</summary>
    public const string Root = "/studio/";

    private const string Prefix = "studio/";

    /// <summary>
    /// What the page may do. Scripts and styles only from this daemon, so
    /// nothing a page could inject runs. Inline styles are allowed because
    /// the terminal sets its own dimensions with a style element; nothing
    /// that matters can be done with a style.
    /// </summary>
    private const string Policy =
        "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; "
        + "img-src 'self' data:; font-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'none'; "
        + "frame-ancestors 'none'";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Names = new(Index);

    /// <summary>Whether this build carries Studio at all.</summary>
    public static bool Built => Names.Value.ContainsKey("index.html");

    /// <summary>The resource name for a path under <see cref="Root"/>, or null where there is none.</summary>
    internal static string? Find(string relative) =>
        Names.Value.TryGetValue(relative.Length == 0 ? "index.html" : relative, out var name) ? name : null;

    /// <summary>Answers a GET for <see cref="Root"/> or anything under it.</summary>
    public static async Task ServeAsync(HttpListenerContext context, string path)
    {
        var relative = Uri.UnescapeDataString(path[Root.Length..]);

        if (!Built)
        {
            await DashboardServer.WriteAsync(context, 404, "text/plain; charset=utf-8",
                "Studio was not built into this launcher, which happens when it is built without Node. "
                + "The classic dashboard is at /.").ConfigureAwait(false);

            return;
        }

        // A route inside the page is in the address's hash, so anything here
        // that is not a file is a mistake rather than a page to fall back to.
        if (Find(relative) is not { } name)
        {
            await DashboardServer.WriteAsync(context, 404, "text/plain; charset=utf-8", "There is nothing here.")
                .ConfigureAwait(false);

            return;
        }

        await using var stream = typeof(StudioAssets).Assembly.GetManifestResourceStream(name)!;
        var response = context.Response;

        response.StatusCode = 200;
        response.ContentType = TypeOf(name);
        response.ContentLength64 = stream.Length;
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Content-Security-Policy"] = Policy;

        // The bundle's own files are named by their content, so a new build
        // is a new name and the old one can be kept for good. The page and
        // the manifest keep their names and so are always asked for again.
        response.Headers["Cache-Control"] = relative.StartsWith("assets/", StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable"
            : "no-cache";

        await stream.CopyToAsync(response.OutputStream).ConfigureAwait(false);
        response.Close();
    }

    /// <summary>Studio's files by their path under <see cref="Root"/>.</summary>
    /// <remarks>
    /// MSBuild writes the directory part of a resource name with the
    /// separator of the machine that built it, so a Windows build names
    /// <c>studio/assets\index.js</c>. Both are read as a slash.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> Index() =>
        typeof(StudioAssets).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(name => name[Prefix.Length..].Replace('\\', '/'), name => name, StringComparer.Ordinal);

    private static string TypeOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".woff2" => "font/woff2",
        ".json" => "application/json; charset=utf-8",
        ".webmanifest" => "application/manifest+json; charset=utf-8",
        _ => "application/octet-stream",
    };
}

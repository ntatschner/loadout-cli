using System.Text.Json.Serialization;

namespace Loadout.Core.Mcp;

/// <summary>Where a server definition came from, and therefore who it applies to.</summary>
public enum McpScope
{
    /// <summary>Declared in the workspace for every project.</summary>
    Global,

    /// <summary>Declared in the workspace for one project.</summary>
    Project,

    /// <summary>
    /// Already present on the machine: an account connector, an installed
    /// plugin, or something configured locally. Not the workspace's to change,
    /// but very much its business to warn about.
    /// </summary>
    Installed,
}

/// <summary>
/// One MCP server, as Claude's own configuration files describe it.
/// <para>
/// The shape is Claude's, not ours: these files are handed to it with
/// <c>--mcp-config</c>, so the property names have to be the ones it reads.
/// Everything is nullable because a stdio server and an HTTP server share no
/// fields beyond a name.
/// </para>
/// </summary>
public sealed class McpServer
{
    /// <summary>
    /// How these files are actually written and read.
    /// <para>
    /// The keys are lower-case in Claude's own files, and System.Text.Json
    /// matches property names case-sensitively unless told otherwise. Without
    /// this every field deserialised to null and every file written here came
    /// out with capitalised keys the agent would not read — which looked, from
    /// the outside, exactly like a server that had no command.
    /// </para>
    /// </summary>
    public static readonly System.Text.Json.JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary><c>stdio</c>, <c>http</c> or <c>sse</c>. Absent means stdio.</summary>
    public string? Type { get; set; }

    /// <summary>Executable for a stdio server.</summary>
    public string? Command { get; set; }

    public List<string>? Args { get; set; }

    /// <summary>Endpoint for an HTTP or SSE server.</summary>
    public string? Url { get; set; }

    public Dictionary<string, string>? Env { get; set; }

    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// What this server is reached at, whichever transport it uses. Two
    /// definitions with the same identity are the same upstream service even
    /// when they are registered under different names.
    /// </summary>
    [JsonIgnore]
    public string Identity =>
        Url is { Length: > 0 } url
            ? Normalise(url)
            : string.Join(' ', new[] { Command }.Concat(Args ?? []).Where(p => p is { Length: > 0 }));

    /// <summary>
    /// Reduces an endpoint to something comparable. Two connectors to one
    /// service routinely differ by a trailing slash or the case of the host,
    /// and reporting those as different servers would make the clash check
    /// useless on exactly the cases it exists for.
    /// </summary>
    private static string Normalise(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            ? $"{parsed.Scheme.ToLowerInvariant()}://{parsed.Host.ToLowerInvariant()}{parsed.AbsolutePath.TrimEnd('/')}"
            : trimmed.ToLowerInvariant();
    }
}

/// <summary>A server together with where it was declared.</summary>
/// <param name="Name">The name Claude registers it under, and the prefix its tools carry.</param>
/// <param name="Scope">Which layer declared it.</param>
/// <param name="Server">The definition itself.</param>
public sealed record McpEntry(string Name, McpScope Scope, McpServer Server);

/// <summary>How serious an MCP finding is.</summary>
public enum McpClashKind
{
    /// <summary>
    /// One name declared in two layers. The narrower one wins, which is
    /// usually intended, but silently so.
    /// </summary>
    ShadowedName,

    /// <summary>
    /// Two names reaching the same service. Both sets of tools load, the model
    /// sees each capability twice, and every session pays for both.
    /// </summary>
    DuplicateService,

    /// <summary>
    /// A command carrying an absolute path. It cannot be right on more than one
    /// machine, and the workspace is shared between machines by design.
    /// </summary>
    MachineSpecificPath,
}

/// <summary>Something worth saying about the set of servers a project would load.</summary>
/// <param name="Kind">What sort of problem it is.</param>
/// <param name="Names">The servers involved.</param>
/// <param name="Detail">What it means, in the words shown to a person.</param>
public sealed record McpClash(McpClashKind Kind, IReadOnlyList<string> Names, string Detail);

using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Mcp;

/// <summary>What a project would load, and anything wrong with it.</summary>
/// <param name="Servers">Every server that applies, narrower scope last.</param>
/// <param name="Clashes">Problems with the set as a whole.</param>
public sealed record McpResolution(IReadOnlyList<McpEntry> Servers, IReadOnlyList<McpClash> Clashes);

/// <summary>Reads and writes the MCP servers a project loads.</summary>
public interface IMcpService
{
    /// <summary>
    /// Everything that applies to a project, with the clashes between them.
    /// </summary>
    /// <param name="slug">Project to resolve for.</param>
    /// <param name="installed">
    /// Servers the agent already has, so a clash with an account connector or
    /// an installed plugin is reported alongside one inside the workspace. The
    /// workspace cannot see these, and they are where the duplicates come from.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<McpResolution>> ResolveAsync(
        string slug,
        IReadOnlyList<McpEntry>? installed = null,
        CancellationToken ct = default);

    /// <summary>Adds or replaces a server in one scope.</summary>
    Task<OperationResult> AddAsync(
        string slug,
        McpScope scope,
        string name,
        McpServer server,
        CancellationToken ct = default);

    /// <summary>Removes a server from one scope. Absent is not an error.</summary>
    Task<OperationResult<bool>> RemoveAsync(
        string slug,
        McpScope scope,
        string name,
        CancellationToken ct = default);

    /// <summary>
    /// The files to hand Claude with <c>--mcp-config</c>, widest scope first,
    /// or empty when the workspace declares none.
    /// </summary>
    IReadOnlyList<string> ConfigFiles(string slug);
}

/// <summary>
/// The MCP servers a project loads, held in the workspace rather than in the
/// repository or in whatever a machine happened to accumulate.
/// <para>
/// Claude reads servers from several places at once — an account's connectors,
/// installed plugins, a project file, a user file — and nothing reconciles
/// them. The result on a working machine was Context7 reachable twice under
/// two names, and a stdio server whose command had an absolute path baked into
/// it. Neither is visible until something behaves oddly.
/// </para>
/// <para>
/// So this does two things: it keeps the declarations somewhere shared and
/// versioned, and it says what is wrong with the set before a session pays for
/// it. It does not silently reconcile anything — which server should win is a
/// decision, and the launcher is not the place to make it on somebody's behalf.
/// </para>
/// </summary>
public sealed class McpService : IMcpService
{
    /// <summary>The file name Claude uses, kept so the files are recognisable.</summary>
    private const string FileName = "mcp.json";

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IWorkspaceManager _workspace;

    public McpService(IWorkspaceManager workspace) => _workspace = workspace;

    /// <inheritdoc />
    public IReadOnlyList<string> ConfigFiles(string slug)
    {
        var files = new List<string>();

        // Widest first: Claude applies later files over earlier ones, which is
        // the same order the shadowing check reports.
        foreach (var path in new[] { PathFor(McpScope.Global, slug), PathFor(McpScope.Project, slug) })
        {
            if (File.Exists(path))
            {
                files.Add(path);
            }
        }

        return files;
    }

    /// <inheritdoc />
    public async Task<OperationResult<McpResolution>> ResolveAsync(
        string slug,
        IReadOnlyList<McpEntry>? installed = null,
        CancellationToken ct = default)
    {
        var entries = new List<McpEntry>(installed ?? []);

        foreach (var scope in new[] { McpScope.Global, McpScope.Project })
        {
            var read = await ReadAsync(PathFor(scope, slug), ct).ConfigureAwait(false);

            if (read.Failed)
            {
                return OperationResult<McpResolution>.Fail(read.Error!, read.ExitCode);
            }

            foreach (var (name, server) in read.Value!)
            {
                entries.Add(new McpEntry(name, scope, server));
            }
        }

        return OperationResult<McpResolution>.Ok(
            new McpResolution(entries, Inspect(entries)));
    }

    /// <summary>
    /// Finds what is wrong with a set of servers.
    /// <para>
    /// Reported, never corrected. Two routes to one service might be
    /// deliberate while a connector is being replaced, and which of two
    /// same-named servers should win is a judgement. Saying so is useful;
    /// choosing silently is not.
    /// </para>
    /// </summary>
    public static IReadOnlyList<McpClash> Inspect(IReadOnlyList<McpEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var clashes = new List<McpClash>();

        foreach (var group in entries.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() < 2)
            {
                continue;
            }

            var scopes = string.Join(" and ", group.Select(e => e.Scope.ToString().ToLowerInvariant()));

            // Deliberately not saying which one wins. Between two workspace
            // scopes the narrower does, but against something already on the
            // machine that depends on how the agent was started, and claiming
            // an order that turns out to be wrong is worse than not saying.
            clashes.Add(new McpClash(
                McpClashKind.ShadowedName,
                [group.Key],
                $"declared in {scopes}; one of them will not load, and which is not obvious"));
        }

        foreach (var group in entries
            .Where(e => e.Server.Identity is { Length: > 0 })
            .GroupBy(e => e.Server.Identity, StringComparer.OrdinalIgnoreCase))
        {
            var names = group.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (names.Count < 2)
            {
                continue;
            }

            clashes.Add(new McpClash(
                McpClashKind.DuplicateService,
                names,
                "the same service under more than one name, so every tool it offers "
                + "is loaded twice and the model sees each one twice"));
        }

        foreach (var entry in entries)
        {
            // Only what the workspace holds. A locally installed server naming
            // an absolute path is correct — it was configured for this machine
            // and travels nowhere. Flagging those would bury the ones that do
            // travel under noise about the ones that do not.
            if (entry.Scope == McpScope.Installed)
            {
                continue;
            }

            var command = entry.Server.Command;

            if (command is { Length: > 0 } && LooksMachineSpecific(command))
            {
                clashes.Add(new McpClash(
                    McpClashKind.MachineSpecificPath,
                    [entry.Name],
                    "its command names an absolute path, which cannot be right on "
                    + "another machine that clones this workspace"));

                continue;
            }

            var pinned = (entry.Server.Args ?? []).FirstOrDefault(LooksMachineSpecific);

            if (pinned is not null)
            {
                clashes.Add(new McpClash(
                    McpClashKind.MachineSpecificPath,
                    [entry.Name],
                    "an argument names an absolute path, which cannot be right on "
                    + "another machine that clones this workspace"));
            }
        }

        return clashes;
    }

    /// <summary>
    /// Whether a string is an absolute path. Checked for both platforms
    /// regardless of the host, because the workspace is shared between them and
    /// a Windows path is just as wrong on Linux as the reverse.
    /// </summary>
    private static bool LooksMachineSpecific(string value) =>
        value.Length > 2
        && ((char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/'))
            || value.StartsWith("/home/", StringComparison.Ordinal)
            || value.StartsWith("/Users/", StringComparison.Ordinal));

    /// <inheritdoc />
    public async Task<OperationResult> AddAsync(
        string slug,
        McpScope scope,
        string name,
        McpServer server,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(server);

        var path = PathFor(scope, slug);

        var read = await ReadAsync(path, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult.Fail(read.Error!, read.ExitCode);
        }

        var servers = new Dictionary<string, McpServer>(read.Value!, StringComparer.Ordinal)
        {
            [name] = server,
        };

        return await WriteAsync(path, servers, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> RemoveAsync(
        string slug,
        McpScope scope,
        string name,
        CancellationToken ct = default)
    {
        var path = PathFor(scope, slug);

        if (!File.Exists(path))
        {
            return OperationResult<bool>.Ok(false);
        }

        var read = await ReadAsync(path, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<bool>.Fail(read.Error!, read.ExitCode);
        }

        var servers = new Dictionary<string, McpServer>(read.Value!, StringComparer.Ordinal);

        if (!servers.Remove(name))
        {
            return OperationResult<bool>.Ok(false);
        }

        var written = await WriteAsync(path, servers, ct).ConfigureAwait(false);

        return written.Failed
            ? OperationResult<bool>.Fail(written.Error!, written.ExitCode)
            : OperationResult<bool>.Ok(true);
    }

    private string PathFor(McpScope scope, string slug) =>
        scope == McpScope.Global
            ? Path.Combine(_workspace.LocalPath, "global", "agents", "claude", FileName)
            : Path.Combine(_workspace.LocalPath, "projects", slug, "agents", "claude", FileName);

    /// <summary>
    /// Reads one file. A missing one is an empty set rather than an error: most
    /// projects declare nothing, and that is not a fault.
    /// </summary>
    private static async Task<OperationResult<Dictionary<string, McpServer>>> ReadAsync(
        string path,
        CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return OperationResult<Dictionary<string, McpServer>>.Ok(new Dictionary<string, McpServer>());
        }

        try
        {
            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                return OperationResult<Dictionary<string, McpServer>>.Ok(
                    new Dictionary<string, McpServer>());
            }

            using var document = JsonDocument.Parse(text, Lenient);

            if (!document.RootElement.TryGetProperty("mcpServers", out var servers)
                || servers.ValueKind != JsonValueKind.Object)
            {
                return OperationResult<Dictionary<string, McpServer>>.Fail(
                    $"{path} has no mcpServers object, so it is not an MCP configuration.",
                    ExitCode.ConfigurationInvalid);
            }

            var parsed = new Dictionary<string, McpServer>(StringComparer.Ordinal);

            foreach (var property in servers.EnumerateObject())
            {
                var server = property.Value.Deserialize<McpServer>(McpServer.Json);

                if (server is not null)
                {
                    parsed[property.Name] = server;
                }
            }

            return OperationResult<Dictionary<string, McpServer>>.Ok(parsed);
        }
        catch (JsonException ex)
        {
            // Loudly. An unreadable server list means the session starts
            // without tools it was expected to have, and a silent empty set
            // would look like the servers were simply never configured.
            return OperationResult<Dictionary<string, McpServer>>.Fail(
                $"{path} is not valid JSON: {ex.Message}", ExitCode.ConfigurationInvalid);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<Dictionary<string, McpServer>>.Fail(
                $"Could not read {path}: {ex.Message}");
        }
    }

    private static async Task<OperationResult> WriteAsync(
        string path,
        Dictionary<string, McpServer> servers,
        CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var document = new Dictionary<string, object> { ["mcpServers"] = servers };

            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, McpServer.Json), ct)
                .ConfigureAwait(false);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not write {path}: {ex.Message}");
        }
    }
}

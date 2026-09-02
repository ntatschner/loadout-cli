using System.Text.Json;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Mcp;

/// <summary>Reads the MCP servers an agent already has, whoever configured them.</summary>
public interface IInstalledMcpReader
{
    /// <summary>
    /// Every server the agent would load without the workspace saying anything:
    /// account connectors, installed plugins, and whatever the machine has
    /// accumulated.
    /// </summary>
    Task<IReadOnlyList<McpEntry>> ReadAsync(string repositoryPath, CancellationToken ct = default);
}

/// <summary>
/// What the agent already has, so that adding a server can say what it collides
/// with.
/// <para>
/// This is the half the workspace cannot see. A project can declare its servers
/// carefully and still end up loading the same service twice, because the
/// duplicate arrived as an account connector or an installed plugin — neither
/// of which the workspace knows about. On a real machine that is exactly what
/// happened: Context7 reachable both as a connector and as a plugin, every tool
/// it offers loaded twice.
/// </para>
/// <para>
/// Two sources, because neither is complete. The agent's own configuration file
/// is structured and reliable but holds only what was configured locally; its
/// <c>mcp list</c> output also shows connectors and plugins but is meant for
/// people to read. Everything here is best-effort by construction: this informs
/// a warning, and a warning that cannot be produced must not stop a launch.
/// </para>
/// </summary>
internal sealed class InstalledMcpReader : IInstalledMcpReader
{
    /// <summary>
    /// How long the agent may take to list its servers. It health-checks each
    /// one, so a hanging remote server must not hang the launcher with it.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;
    private readonly IEnvironmentProvider _environment;

    public InstalledMcpReader(
        IProcessLauncher processes,
        IExecutableResolver resolver,
        IEnvironmentProvider environment)
    {
        _processes = processes;
        _resolver = resolver;
        _environment = environment;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpEntry>> ReadAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        var found = new Dictionary<string, McpEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ReadConfigured(repositoryPath))
        {
            found[entry.Name] = entry;
        }

        foreach (var entry in ReadEnabledPlugins())
        {
            if (!found.ContainsKey(entry.Name))
            {
                found[entry.Name] = entry;
            }
        }

        foreach (var entry in await ListedAsync(repositoryPath, ct).ConfigureAwait(false))
        {
            // The file wins where both know a server: it carries the actual
            // command and arguments, where the listing carries a summary.
            if (!found.ContainsKey(entry.Name))
            {
                found[entry.Name] = entry;
            }
        }

        return [.. found.Values];
    }

    /// <summary>
    /// Reads the agent's own configuration file: the user's servers, and the
    /// ones recorded against this repository.
    /// </summary>
    private IReadOnlyList<McpEntry> ReadConfigured(string repositoryPath)
    {
        var path = Path.Combine(_environment.HomeDirectory, ".claude.json");

        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            var entries = new List<McpEntry>();

            Collect(document.RootElement, entries);

            if (document.RootElement.TryGetProperty("projects", out var projects)
                && projects.ValueKind == JsonValueKind.Object)
            {
                foreach (var project in projects.EnumerateObject())
                {
                    // Recorded under the path the agent was run in, which is
                    // written with either separator depending on the day.
                    if (SamePath(project.Name, repositoryPath))
                    {
                        Collect(project.Value, entries);
                    }
                }
            }

            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Somebody else's file, in a format nobody promised us. Failing to
            // read it costs a warning, not a launch.
            return [];
        }
    }

    private static void Collect(JsonElement parent, List<McpEntry> into)
    {
        if (!parent.TryGetProperty("mcpServers", out var servers)
            || servers.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in servers.EnumerateObject())
        {
            try
            {
                if (property.Value.Deserialize<McpServer>(McpServer.Json) is { } server)
                {
                    into.Add(new McpEntry(property.Name, McpScope.Installed, server));
                }
            }
            catch (JsonException)
            {
                // One unreadable entry costs that entry.
            }
        }
    }

    /// <summary>
    /// Servers brought in by plugins that are switched on.
    /// <para>
    /// These appear nowhere else: the agent's listing does not show them and
    /// its configuration file does not mention them, yet they are a real source
    /// of duplication — a plugin reaching the same service as an account
    /// connector loads every tool twice. Only enabled plugins are read, because
    /// a plugin that is installed and switched off contributes nothing, and
    /// reporting its servers would be a warning about something that is not
    /// happening.
    /// </para>
    /// </summary>
    private IReadOnlyList<McpEntry> ReadEnabledPlugins()
    {
        var root = Path.Combine(Agents.AgentHome.Claude(_environment), "plugins");
        var settings = Agents.AgentHome.ClaudeSettings(_environment);

        if (!Directory.Exists(root) || !File.Exists(settings))
        {
            return [];
        }

        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settings));

            if (document.RootElement.TryGetProperty("enabledPlugins", out var plugins)
                && plugins.ValueKind == JsonValueKind.Object)
            {
                foreach (var plugin in plugins.EnumerateObject())
                {
                    if (plugin.Value.ValueKind == JsonValueKind.True)
                    {
                        // Recorded as name@marketplace; the directory is named
                        // after the first half.
                        enabled.Add(plugin.Name.Split('@')[0]);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }

        if (enabled.Count == 0)
        {
            return [];
        }

        var entries = new List<McpEntry>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, ".mcp.json", SearchOption.AllDirectories))
            {
                // Matched on the path rather than on a manifest, because the
                // directory a plugin unpacks into is named after it and there
                // is no index tying the two together.
                var path = file.Replace(Path.DirectorySeparatorChar, '/');

                if (!enabled.Any(name =>
                        path.Contains($"/{name}/", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(file));

                Collect(document.RootElement, entries);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Best effort throughout: this informs a warning.
        }

        return entries;
    }

    /// <summary>
    /// Asks the agent what it has. This is the only way to see account
    /// connectors, which appear in no file the launcher can find.
    /// </summary>
    private async Task<IReadOnlyList<McpEntry>> ListedAsync(
        string repositoryPath,
        CancellationToken ct)
    {
        var executable = _resolver.Resolve("claude");

        if (executable is null)
        {
            return [];
        }

        ProcessOutcome result;

        try
        {
            var run = await _processes
                .RunAsync(new ProcessRequest(executable, ["mcp", "list"], repositoryPath), Patience, ct)
                .ConfigureAwait(false);

            if (run.Failed || run.Value is null)
            {
                return [];
            }

            result = run.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }

        var entries = new List<McpEntry>();

        foreach (var line in result.StandardOutput.Split('\n'))
        {
            if (Parse(line) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// Reads one line of the agent's listing, which is written for a person:
    /// <c>name: target - status</c>.
    /// </summary>
    /// <remarks>
    /// Parsing another program's human output is a poor contract, and it is
    /// used here only because there is no other way to see the connectors. A
    /// line that does not fit the shape is skipped rather than guessed at, so a
    /// change to the format costs the warning and nothing else.
    /// </remarks>
    private static McpEntry? Parse(string line)
    {
        var trimmed = line.Trim();

        var colon = trimmed.IndexOf(": ", StringComparison.Ordinal);

        if (colon <= 0)
        {
            return null;
        }

        var name = trimmed[..colon].Trim();
        var rest = trimmed[(colon + 2)..];

        // The status is appended after the target with a dash. Cut at the last
        // one, because a command or URL can contain dashes of its own.
        var dash = rest.LastIndexOf(" - ", StringComparison.Ordinal);
        var target = (dash > 0 ? rest[..dash] : rest).Trim();

        if (name.Length == 0 || target.Length == 0)
        {
            return null;
        }

        var server = target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new McpServer { Type = "http", Url = target }
            : Command(target);

        return new McpEntry(name, McpScope.Installed, server);
    }

    /// <summary>Splits a listed command back into a program and its arguments.</summary>
    private static McpServer Command(string target)
    {
        var parts = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new McpServer
        {
            Command = parts.FirstOrDefault(),
            Args = [.. parts.Skip(1)],
        };
    }

    /// <summary>
    /// Whether two paths name the same directory, allowing for the separator
    /// each was written with.
    /// </summary>
    private static bool SamePath(string left, string right) =>
        string.Equals(
            left.Replace('\\', '/').TrimEnd('/'),
            right.Replace('\\', '/').TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
}

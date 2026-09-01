using System.Text.Json;

namespace Loadout.Agents;

/// <summary>
/// Declares the launcher's own MCP server to the agent it is about to start.
/// </summary>
/// <remarks>
/// <para>
/// Written into the launch's runtime directory rather than the workspace, and
/// deliberately: it names the executable running right now, and an absolute
/// path baked into a shared file is exactly the fault the MCP service exists to
/// warn about — correct on the machine that wrote it and wrong on every other
/// one that clones the workspace. The runtime directory is removed when the
/// agent exits, so this cannot outlive the session that needed it.
/// </para>
/// <para>
/// Nothing is declared when the setting is off, and nothing when the executable
/// cannot be found: a server entry pointing at a path that is not there fails
/// the agent's own startup rather than the launcher's, which is a confusing
/// place to discover it.
/// </para>
/// </remarks>
public static class SelfServerConfig
{
    /// <summary>The file written beside the compiled context, when one is written.</summary>
    public const string FileName = "loadout-mcp.json";

    /// <summary>
    /// Writes the declaration and returns its path, or nothing at all.
    /// </summary>
    /// <param name="enabled">Whether the launcher serves its own tools.</param>
    /// <param name="slug">Project the served tools answer about.</param>
    /// <param name="runtimeDirectory">Where this launch keeps its working files.</param>
    /// <param name="warnings">Told why, when there is nothing to declare.</param>
    /// <param name="executablePath">
    /// The launcher's own path. Defaults to this process, and is a parameter so
    /// a test can say what it is rather than depend on what is running it.
    /// </param>
    public static IReadOnlyList<string> Write(
        bool enabled,
        string slug,
        string runtimeDirectory,
        List<string> warnings,
        string? executablePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentNullException.ThrowIfNull(warnings);

        if (!enabled)
        {
            return [];
        }

        var executable = executablePath ?? Environment.ProcessPath;

        if (executable is not { Length: > 0 } || !File.Exists(executable))
        {
            warnings.Add(
                "The launcher could not work out its own path, so its tools were not offered to "
                + "the agent. Everything else about the launch is unaffected.");

            return [];
        }

        var path = Path.Combine(runtimeDirectory, FileName);

        var document = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["loadout"] = new
                {
                    command = executable,
                    args = new[] { "mcp", "serve", "--project", slug },
                },
            },
        };

        try
        {
            Directory.CreateDirectory(runtimeDirectory);

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    document,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"The launcher's own tools could not be offered: {exception.Message}");

            return [];
        }

        return [path];
    }
}

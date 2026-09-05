using System.Text.Json;
using Loadout.Models;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Manager;

/// <summary>One plugin the agent has installed.</summary>
/// <param name="Id">Its full id, which carries the marketplace it came from.</param>
/// <param name="Version">The installed version.</param>
/// <param name="Scope">Where it applies: the user, or one project.</param>
/// <param name="Enabled">Whether it is switched on.</param>
public sealed record InstalledPlugin(string Id, string Version, string Scope, bool Enabled);

/// <summary>What the agent has installed as plugins.</summary>
public interface IInstalledPluginReader
{
    Task<IReadOnlyList<InstalledPlugin>> ReadAsync(
        string repositoryPath,
        CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// Asked of the agent rather than read off disk, for the reason the MCP reader
/// gives: the layout under <c>~/.claude/plugins</c> is not a published contract
/// and a marketplace can put things there this would have to guess at. The
/// agent already answers the question, so it is asked.
/// </para>
/// <para>
/// Best effort by construction. No agent installed, an agent too old to know
/// the command, output that is not the JSON expected — each of those is an
/// empty list rather than a failure, because "what is loaded" is still a useful
/// screen with this section blank and useless if one missing binary empties the
/// whole of it.
/// </para>
/// </remarks>
public sealed class InstalledPluginReader : IInstalledPluginReader
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;

    public InstalledPluginReader(IProcessLauncher processes, IExecutableResolver resolver)
    {
        _processes = processes;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstalledPlugin>> ReadAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        var executable = _resolver.Resolve("claude");

        if (executable is null)
        {
            return [];
        }

        ProcessOutcome outcome;

        try
        {
            var run = await _processes
                .RunAsync(
                    new ProcessRequest(executable, ["plugin", "list", "--json"], repositoryPath),
                    Patience,
                    ct)
                .ConfigureAwait(false);

            if (run.Failed || run.Value is null)
            {
                return [];
            }

            outcome = run.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }

        return Parse(outcome.StandardOutput);
    }

    /// <summary>
    /// Reads the listing, taking only the fields this screen shows.
    /// </summary>
    /// <remarks>
    /// Deliberately tolerant of fields it does not know. The shape is the
    /// agent's and can gain properties without warning; a reader that insisted
    /// on the whole of it would empty this section the first time one appeared.
    /// </remarks>
    internal static IReadOnlyList<InstalledPlugin> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var plugins = new List<InstalledPlugin>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("id", out var id)
                    || id.GetString() is not { Length: > 0 } name)
                {
                    continue;
                }

                plugins.Add(new InstalledPlugin(
                    name,
                    Text(element, "version"),
                    Text(element, "scope") is { Length: > 0 } scope ? scope : "user",
                    element.TryGetProperty("enabled", out var enabled)
                        && enabled.ValueKind == JsonValueKind.True));
            }

            return plugins;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}

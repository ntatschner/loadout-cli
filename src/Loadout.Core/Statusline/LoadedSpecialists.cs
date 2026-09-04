using Loadout.Core.Configuration;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Statusline;

/// <summary>
/// What was composed into a session, written down at launch.
/// </summary>
/// <remarks>
/// Written rather than worked out, for the same reason the spending figure is:
/// resolving the library takes about half a second, and the status line is
/// redrawn on every prompt. Half a second per keystroke is not a status line.
/// </remarks>
public sealed class LoadedSpecialists
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>When the launch that composed these happened.</summary>
    public DateTimeOffset LoadedUtc { get; set; }

    /// <summary>The specialist ids, in composition order.</summary>
    public List<string> Ids { get; set; } = [];

    /// <summary>The mode the session was started in, when it named one.</summary>
    public string Mode { get; set; } = string.Empty;
}

/// <summary>Keeps what a project's last launch composed.</summary>
public interface ILoadedSpecialistStore
{
    /// <summary>What the last launch composed, or null when nothing has.</summary>
    Task<LoadedSpecialists?> ReadAsync(string projectSlug, CancellationToken ct = default);

    /// <summary>Records what a launch composed.</summary>
    Task WriteAsync(
        string projectSlug,
        IReadOnlyList<string> ids,
        string? mode,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class LoadedSpecialistStore : ILoadedSpecialistStore
{
    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public LoadedSpecialistStore(IPlatformPaths paths, YamlStore yaml, TimeProvider time)
    {
        _paths = paths;
        _yaml = yaml;
        _time = time;
    }

    private string PathFor(string slug) =>
        Path.Combine(_paths.Paths.State, "specialists", slug + ".yaml");

    /// <inheritdoc />
    public async Task<LoadedSpecialists?> ReadAsync(
        string projectSlug,
        CancellationToken ct = default)
    {
        var path = PathFor(projectSlug);

        if (!File.Exists(path))
        {
            return null;
        }

        var loaded = await _yaml
            .LoadAsync(path, () => new LoadedSpecialists(), ct)
            .ConfigureAwait(false);

        // No answer rather than a wrong one. This is on the path that draws
        // somebody's prompt, and nothing here is worth a prompt not rendering.
        return loaded.Succeeded && loaded.Value!.LoadedUtc != default ? loaded.Value : null;
    }

    /// <inheritdoc />
    public Task WriteAsync(
        string projectSlug,
        IReadOnlyList<string> ids,
        string? mode,
        CancellationToken ct = default) =>
        _yaml.SaveAsync(
            PathFor(projectSlug),
            new LoadedSpecialists
            {
                LoadedUtc = _time.GetUtcNow(),
                Ids = [.. ids],
                Mode = mode?.Trim() ?? string.Empty,
            },
            true,
            ct);
}

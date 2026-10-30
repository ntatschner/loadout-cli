using Loadout.Core.Agents;
using Loadout.Core.Updates;
using Loadout.Models.Results;

namespace Loadout.Core.About;

/// <summary>What the About page shows, gathered once for every surface.</summary>
public interface IAboutService
{
    /// <summary>The installed version, the feature matrix, the guides and the release timeline.</summary>
    /// <param name="refresh">Ask GitHub for the releases even when the kept list is fresh.</param>
    /// <param name="ct">Stops the work.</param>
    Task<OperationResult<AboutModel>> GetAsync(bool refresh = false, CancellationToken ct = default);

    /// <summary>As <see cref="GetAsync(bool, CancellationToken)"/>, asking no one when <paramref name="offline"/>.</summary>
    Task<OperationResult<AboutModel>> GetAsync(bool refresh, bool offline, CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class AboutService : IAboutService
{
    private readonly IUpdateNotice _notice;
    private readonly IReleaseTimeline _timeline;
    private readonly string _currentVersion;
    private readonly bool _devBuild;

    public AboutService(
        IUpdateNotice notice,
        IReleaseTimeline timeline,
        string? currentVersion = null,
        bool? devBuild = null)
    {
        _notice = notice;
        _timeline = timeline;
        _currentVersion = currentVersion
            ?? typeof(AboutService).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        // A development build runs under the dotnet host, which is the one
        // case Parts() gives a prefix for.
        _devBuild = devBuild ?? LauncherInvocation.Parts() is { Prefix.Count: > 0 };
    }

    /// <inheritdoc />
    public Task<OperationResult<AboutModel>> GetAsync(bool refresh = false, CancellationToken ct = default) =>
        GetAsync(refresh, offline: false, ct);

    /// <inheritdoc />
    public async Task<OperationResult<AboutModel>> GetAsync(bool refresh, bool offline, CancellationToken ct = default)
    {
        // The notice's cached answer rather than a fresh check: this page is
        // opened to look, and the update itself checks again before it acts.
        var available = offline ? null : await _notice.AvailableAsync(ct).ConfigureAwait(false);

        var timeline = await _timeline.ReadAsync(refresh, offline, ct).ConfigureAwait(false);

        if (timeline.Empty == TimelineEmpty.NotGitHub)
        {
            // No list to ask for, but what is running and what is offered are
            // still known, so the timeline holds those two.
            var known = new List<AboutRelease>();

            if (available is not null)
            {
                known.Add(new AboutRelease(available, available, null, string.Empty, string.Empty, false, Kind(available)));
            }

            known.Add(new AboutRelease(_currentVersion, _currentVersion, null, string.Empty, string.Empty, true, Kind(_currentVersion)));

            timeline = timeline with { Releases = known };
        }

        var features = AboutManifest.Features()
            .Select(row => new AboutFeature(
                row.Id,
                row.Name,
                row.Since,
                row.Status,
                row.Guide,
                Installed: !UpdateService.IsNewer(row.Since, _currentVersion)))
            .ToList();

        return OperationResult<AboutModel>.Ok(new AboutModel(
            _currentVersion,
            _devBuild,
            available,
            available is not null,
            features,
            AboutManifest.Guides(),
            timeline));
    }

    private static string Kind(string version) =>
        Version.TryParse(version, out var parsed) && parsed.Build > 0 ? "fix" : "feature";
}

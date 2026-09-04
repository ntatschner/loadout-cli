using Loadout.Core.Configuration;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Usage;

/// <summary>
/// What the thresholds said, and when.
/// </summary>
/// <remarks>
/// Written down because working it out costs seconds and the status line has
/// milliseconds. The time it was computed is part of the record rather than an
/// afterthought: a figure about spending that does not say how old it is
/// invites being read as live, and this one is not.
/// </remarks>
public sealed class SpendNotice
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>When the underlying scan ran.</summary>
    public DateTimeOffset ComputedUtc { get; set; }

    /// <summary>What crossed a threshold. Empty means nothing did.</summary>
    public List<string> Lines { get; set; } = [];
}

/// <summary>Keeps the last answer the thresholds gave, per project.</summary>
public interface ISpendNoticeStore
{
    /// <summary>The last answer for a project, or null when there is none.</summary>
    Task<SpendNotice?> ReadAsync(string projectSlug, CancellationToken ct = default);

    /// <summary>Records an answer.</summary>
    Task WriteAsync(string projectSlug, IReadOnlyList<string> lines, CancellationToken ct = default);

    /// <summary>
    /// Whether a refresh is due, and claims the right to start one.
    /// </summary>
    /// <remarks>
    /// Claiming and asking are the same call on purpose. The status line runs
    /// several times a minute and every one of those would otherwise see the
    /// same stale file and start its own refresh, which is a stampede of
    /// two-second scans over a number nobody needed that badly.
    /// </remarks>
    bool ClaimRefresh(string projectSlug, DateTimeOffset now, TimeSpan after);
}

/// <inheritdoc />
internal sealed class SpendNoticeStore : ISpendNoticeStore
{
    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml;

    public SpendNoticeStore(IPlatformPaths paths, YamlStore yaml)
    {
        _paths = paths;
        _yaml = yaml;
    }

    private string DirectoryPath => Path.Combine(_paths.Paths.State, "spend");

    private string PathFor(string slug) => Path.Combine(DirectoryPath, slug + ".yaml");

    /// <summary>Where the last attempt to refresh was marked.</summary>
    private string ClaimFor(string slug) => Path.Combine(DirectoryPath, slug + ".claim");

    /// <inheritdoc />
    public async Task<SpendNotice?> ReadAsync(string projectSlug, CancellationToken ct = default)
    {
        var path = PathFor(projectSlug);

        if (!File.Exists(path))
        {
            return null;
        }

        var loaded = await _yaml
            .LoadAsync<SpendNotice>(path, () => new SpendNotice(), ct)
            .ConfigureAwait(false);

        // No answer rather than a wrong one. This is on the status line's path,
        // and a broken cache must never be the reason a prompt does not draw.
        return loaded.Succeeded && loaded.Value!.ComputedUtc != default ? loaded.Value : null;
    }

    /// <inheritdoc />
    public Task WriteAsync(
        string projectSlug,
        IReadOnlyList<string> lines,
        CancellationToken ct = default) =>
        _yaml.SaveAsync(
            PathFor(projectSlug),
            new SpendNotice
            {
                ComputedUtc = DateTimeOffset.UtcNow,
                Lines = [.. lines],
            },
            true,
            ct);

    /// <inheritdoc />
    public bool ClaimRefresh(string projectSlug, DateTimeOffset now, TimeSpan after)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);

            var claim = ClaimFor(projectSlug);

            // The time is written into the file rather than taken from its
            // modification stamp. Reading the stamp mixes the caller's clock
            // with the filesystem's, and the two are only ever the same by
            // accident: given a time to reason from, this compares against a
            // time recorded the same way.
            if (File.Exists(claim)
                && DateTimeOffset.TryParse(
                    File.ReadAllText(claim),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var claimed)
                && now - claimed < after)
            {
                return false;
            }

            // Marked before the work starts, not after. Marking afterwards
            // leaves the whole length of the scan for everybody else to see an
            // unclaimed file and start their own.
            File.WriteAllText(claim, now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Somebody else has it, or the directory is not writable. Either
            // way this caller is not the one refreshing, and neither is a
            // reason to bother anybody about it.
            return false;
        }
    }
}

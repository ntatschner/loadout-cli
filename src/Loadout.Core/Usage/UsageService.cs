using Loadout.Core.Projects;
using Loadout.Models.Results;

namespace Loadout.Core.Usage;

/// <summary>How a usage report should be narrowed.</summary>
/// <param name="Days">How many days back to include, counting today.</param>
/// <param name="ProjectSlug">Only this registered project.</param>
/// <param name="Agent">Only this agent.</param>
public sealed record UsageQuery(int Days = 30, string? ProjectSlug = null, string? Agent = null);

/// <summary>One row of a report: something spent tokens, and this is how many.</summary>
/// <param name="Name">What to call it — a slug, a day, a model, an agent.</param>
/// <param name="Totals">What it spent.</param>
/// <param name="IsRegistered">
/// Whether a project row corresponds to a registered project. False for work
/// done somewhere the launcher does not know about, which is worth showing
/// rather than hiding: it is usually the answer to "where did all that go".
/// </param>
public sealed record UsageGroup(string Name, UsageTotals Totals, bool IsRegistered = true);

/// <summary>Everything a usage report has to say.</summary>
/// <param name="Since">The first day included.</param>
/// <param name="Totals">The whole window, added up.</param>
/// <param name="Projects">By project, largest first.</param>
/// <param name="Days">By day, most recent first.</param>
/// <param name="Models">By model, largest first.</param>
/// <param name="Agents">By agent, largest first.</param>
/// <param name="Integrity">Whether to believe any of it.</param>
public sealed record UsageReport(
    DateOnly Since,
    UsageTotals Totals,
    IReadOnlyList<UsageGroup> Projects,
    IReadOnlyList<UsageGroup> Days,
    IReadOnlyList<UsageGroup> Models,
    IReadOnlyList<UsageGroup> Agents,
    UsageIntegrity Integrity);

/// <summary>What the agents spent, across all of them, attributed to projects.</summary>
public interface IUsageService
{
    Task<OperationResult<UsageReport>> ReportAsync(
        UsageQuery query,
        CancellationToken ct = default);
}

/// <summary>
/// Gathers every agent's accounting and says which project it belonged to.
/// </summary>
/// <remarks>
/// The attribution is the part the launcher adds, and it is the same trick the
/// session list plays: an agent records the directory it worked in and nothing
/// else, so raw totals are totals per path. Matching those against the registry
/// turns them into totals per project, which is the only form in which the
/// question "what did this cost" can actually be asked.
/// </remarks>
internal sealed class UsageService : IUsageService
{
    private readonly IReadOnlyList<IUsageHistory> _histories;
    private readonly IProjectService _projects;

    public UsageService(IEnumerable<IUsageHistory> histories, IProjectService projects)
    {
        _histories = histories.ToList();
        _projects = projects;
    }

    /// <inheritdoc />
    public async Task<OperationResult<UsageReport>> ReportAsync(
        UsageQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var days = Math.Max(1, query.Days);
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(days - 1));

        var buckets = new List<UsageBucket>();
        var integrity = UsageIntegrity.Empty;

        foreach (var history in _histories)
        {
            ct.ThrowIfCancellationRequested();

            if (!history.IsAvailable)
            {
                continue;
            }

            if (query.Agent is { Length: > 0 } wanted
                && !string.Equals(history.Agent, wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var scanned = await history.ScanAsync(since, ct).ConfigureAwait(false);

            if (scanned.Failed)
            {
                // One agent's history being unreadable must not hide the
                // other's, but it must not be passed off as zero either.
                integrity += new UsageIntegrity(FilesSkipped: 1);

                continue;
            }

            buckets.AddRange(scanned.Value!.Buckets);
            integrity += scanned.Value.Integrity;
        }

        var attributed = await AttributeAsync(buckets, ct).ConfigureAwait(false);

        if (query.ProjectSlug is { Length: > 0 } slug)
        {
            attributed = attributed
                .Where(row => string.Equals(row.Slug, slug, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var totals = attributed.Aggregate(
            UsageTotals.Zero,
            (running, row) => running + row.Bucket.Totals);

        return OperationResult<UsageReport>.Ok(new UsageReport(
            since,
            totals,
            Group(attributed, row => row.Label, row => row.Slug is not null),
            attributed
                .GroupBy(row => row.Bucket.Day)
                .OrderByDescending(group => group.Key)
                .Select(group => new UsageGroup(
                    group.Key.ToString("yyyy-MM-dd"),
                    group.Aggregate(UsageTotals.Zero, (r, row) => r + row.Bucket.Totals)))
                .ToList(),
            Group(attributed, row => row.Bucket.Model, _ => true),
            Group(attributed, row => row.Bucket.Agent, _ => true),
            integrity));
    }

    /// <summary>Rolls rows up by some name, largest spender first.</summary>
    private static List<UsageGroup> Group(
        List<AttributedBucket> rows,
        Func<AttributedBucket, string> name,
        Func<AttributedBucket, bool> registered) =>
        rows
            .GroupBy(name)
            .Select(group => new UsageGroup(
                group.Key,
                group.Aggregate(UsageTotals.Zero, (running, row) => running + row.Bucket.Totals),
                registered(group.First())))
            .OrderByDescending(group => group.Totals.Total)
            .ToList();

    /// <summary>A bucket with the project it turned out to belong to.</summary>
    private sealed record AttributedBucket(UsageBucket Bucket, string? Slug)
    {
        /// <summary>
        /// The slug when it is one of ours, and the folder's own name when it
        /// is not. A full path here would be the widest column in the table and
        /// the least informative.
        /// </summary>
        internal string Label => Slug ?? Leaf(Bucket.Directory);

        private static string Leaf(string directory)
        {
            var trimmed = directory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            var leaf = Path.GetFileName(trimmed);

            return leaf is { Length: > 0 } ? leaf : trimmed;
        }
    }

    /// <summary>
    /// Fills in which project each directory belongs to, resolving each one
    /// only once — a fortnight's work lands in a handful of repositories, and
    /// every resolution walks the filesystem looking for a root.
    /// </summary>
    private async Task<List<AttributedBucket>> AttributeAsync(
        List<UsageBucket> buckets,
        CancellationToken ct)
    {
        var resolved = new Dictionary<string, string?>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        var attributed = new List<AttributedBucket>(buckets.Count);

        foreach (var bucket in buckets)
        {
            ct.ThrowIfCancellationRequested();

            if (!resolved.TryGetValue(bucket.Directory, out var slug))
            {
                var result = await _projects
                    .ResolveFromDirectoryAsync(bucket.Directory, ct)
                    .ConfigureAwait(false);

                slug = result.Succeeded ? result.Value?.Entry.Slug : null;

                resolved[bucket.Directory] = slug;
            }

            attributed.Add(new AttributedBucket(bucket, slug));
        }

        return attributed;
    }
}

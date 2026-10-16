using System.Globalization;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Unix;

/// <summary>
/// Every process with its parent and resident set, from <c>ps</c>, which
/// Linux and macOS both have and both spell the same way.
/// </summary>
public sealed class UnixProcessTree : IProcessTree
{
    private readonly IProcessLauncher _launcher;

    public UnixProcessTree(IProcessLauncher launcher) => _launcher = launcher;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcessEntry>> SnapshotAsync(CancellationToken ct = default)
    {
        var ran = await _launcher
            .RunAsync(new ProcessRequest("ps", ["-A", "-o", "pid=,ppid=,rss="]), TimeSpan.FromSeconds(5), ct)
            .ConfigureAwait(false);

        return ran.Succeeded && ran.Value!.Succeeded ? Parse(ran.Value.StandardOutput) : [];
    }

    /// <summary>Lines of pid, parent and resident kilobytes, as ps prints them.</summary>
    public static IReadOnlyList<ProcessEntry> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var entries = new List<ProcessEntry>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 3
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent))
            {
                entries.Add(new ProcessEntry(
                    id,
                    parent,
                    long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var kilobytes)
                        ? kilobytes * 1024
                        : null));
            }
        }

        return entries;
    }
}
